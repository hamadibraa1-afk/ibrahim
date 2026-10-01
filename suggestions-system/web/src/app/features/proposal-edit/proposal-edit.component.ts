import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentService } from '../../core/services/attachment.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import { Proposal, ProposalStatus } from '../../core/models/proposal.model';
import { FormSettingsService } from '../../core/services/form-settings.service';
import { FormField } from '../../core/models/form-field.model';

/** تعديل مقترح — يُستخدم للمسودات وللمقترحات المعادة من الفرز. */
@Component({
  selector: 'app-proposal-edit',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, AttachmentListComponent],
  templateUrl: './proposal-edit.component.html',
})
export class ProposalEditComponent implements OnInit {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private location = inject(Location);
  private proposalService = inject(ProposalService);
  private attachmentService = inject(AttachmentService);
  private formSettings = inject(FormSettingsService);

  readonly ProposalStatus = ProposalStatus;
  readonly maxFiles = 10;
  readonly maxFileSizeBytes = 10 * 1024 * 1024;
  readonly maxTotalBytes = 15 * 1024 * 1024;
  readonly allowedExtensions = [
    '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
    '.png', '.jpg', '.jpeg', '.gif', '.webp', '.txt', '.csv', '.zip', '.rar', '.7z',
  ];

  proposal = signal<Proposal | null>(null);
  loading = signal(true);
  saving = signal(false);
  errorMessage = signal<string | null>(null);
  newFiles = signal<File[]>([]);
  customFields = signal<FormField[]>([]);
  impactFields = signal<FormField[]>([]);
  customValues: Record<string, string> = {};

  form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.minLength(6)]],
    implementationMechanism: ['', [Validators.required, Validators.minLength(20)]],
    submissionReasons: ['', [Validators.required, Validators.minLength(20)]],
  });

  ngOnInit() {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.proposalService.getById(id).subscribe({
      next: p => {
        this.proposal.set(p);
        this.form.patchValue({
          title: p.title,
          implementationMechanism: p.implementationMechanism,
          submissionReasons: p.submissionReasons,
        });
        // القيم الحالية للحقول الإضافية
        p.customFields.forEach(cf => (this.customValues[cf.fieldKey] = cf.value ?? ''));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });

    this.formSettings.getAll(true).subscribe({
      next: fields => {
        const active = fields.filter(f => !f.isSystem);
        this.impactFields.set(active.filter(f => f.section === 'Impact'));
        this.customFields.set(active.filter(f => f.section !== 'Impact'));
        active.forEach(f => (this.customValues[f.fieldKey] ??= f.fieldType === 'Checkbox' ? 'false' : ''));
      },
      error: () => {},
    });
  }

  setCheckbox(key: string, checked: boolean) {
    this.customValues[key] = checked ? 'true' : 'false';
  }

  get isResubmission(): boolean {
    return this.proposal()?.status === ProposalStatus.ReturnedForEdit;
  }

  get anyImpactSelected(): boolean {
    return this.impactFields().some(f => this.customValues[f.fieldKey] === 'true');
  }

  get currentTotalSize(): number {
    const existing = this.proposal()?.attachments.reduce((s, a) => s + a.sizeBytes, 0) ?? 0;
    return existing + this.newFiles().reduce((s, f) => s + f.size, 0);
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} بايت`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} كيلوبايت`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} ميجابايت`;
  }

  onFilesSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (!input.files) return;
    this.errorMessage.set(null);

    const current = [...this.newFiles()];
    const existingCount = this.proposal()?.attachments.length ?? 0;

    for (const file of Array.from(input.files)) {
      const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
      if (!this.allowedExtensions.includes(ext)) {
        this.errorMessage.set(`نوع الملف "${file.name}" غير مسموح به.`); continue;
      }
      if (file.size > this.maxFileSizeBytes) {
        this.errorMessage.set(`حجم "${file.name}" يتجاوز 10 ميجابايت.`); continue;
      }
      if (existingCount + current.length >= this.maxFiles) {
        this.errorMessage.set(`الحد الأقصى ${this.maxFiles} ملفات.`); break;
      }
      current.push(file);
    }
    this.newFiles.set(current);
    input.value = '';
  }

  removeNewFile(file: File) {
    this.newFiles.update(list => list.filter(f => f !== file));
  }

  cancel() { this.location.back(); }

  submit() {
    this.errorMessage.set(null);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    if (this.impactFields().length && !this.anyImpactSelected) {
      this.errorMessage.set('الرجاء تحديد أثر واحد على الأقل.'); return;
    }
    const p = this.proposal(); if (!p) return;

    this.saving.set(true);
    this.proposalService.update(p.id, {
      ...this.form.getRawValue(),
      customFields: this.customValues,
    }).subscribe({
      next: () => {
        const files = this.newFiles();
        if (!files.length) { this.router.navigate(['/proposals', p.id]); return; }
        this.attachmentService.upload(p.id, files).subscribe({
          next: () => this.router.navigate(['/proposals', p.id]),
          error: () => {
            this.saving.set(false);
            this.errorMessage.set('حُفظت التعديلات لكن تعذّر رفع بعض المرفقات.');
          },
        });
      },
      error: err => {
        this.saving.set(false);
        this.errorMessage.set(err?.error?.message ?? 'تعذّر حفظ التعديلات.');
      },
    });
  }
}
