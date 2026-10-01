import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentService } from '../../core/services/attachment.service';
import { AuthService } from '../../core/services/auth.service';
import { FormSettingsService } from '../../core/services/form-settings.service';
import { FormField, SubmissionSections } from '../../core/models/form-field.model';

@Component({
  selector: 'app-submission-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule],
  templateUrl: './submission-form.component.html',
})
export class SubmissionFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private proposalService = inject(ProposalService);
  private attachmentService = inject(AttachmentService);
  private formSettings = inject(FormSettingsService);
  private router = inject(Router);
  auth = inject(AuthService);

  readonly maxFiles = 10;
  readonly maxFileSizeBytes = 10 * 1024 * 1024;
  readonly maxTotalBytes = 15 * 1024 * 1024;
  readonly allowedExtensions = [
    '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
    '.png', '.jpg', '.jpeg', '.gif', '.webp', '.txt', '.csv', '.zip', '.rar', '.7z',
  ];

  readonly today = new Date();

  submitting = signal(false);
  errorMessage = signal<string | null>(null);
  selectedFiles = signal<File[]>([]);

  /** الحقول الإضافية المعرَّفة من الإعدادات (غير حقول النظام الأساسية). */
  customFields = signal<FormField[]>([]);
  impactFields = signal<FormField[]>([]);
  customValues: Record<string, string> = {};

  ngOnInit() {
    this.formSettings.getAll(true).subscribe({
      next: fields => {
        // حقول "قياس الأثر الفعلي" تُعبّأ بعد الاعتماد بأشهر، لا عند التقديم
        const active = fields.filter(f => !f.isSystem && SubmissionSections.includes(f.section));
        // "أثر التطبيق" يُعرض كشبكة خانات اختيار في قسم مستقل
        this.impactFields.set(active.filter(f => f.section === 'Impact'));
        this.customFields.set(active.filter(f => f.section !== 'Impact'));
        active.forEach(f => (this.customValues[f.fieldKey] ??= f.fieldType === 'Checkbox' ? 'false' : ''));
      },
      error: () => {},
    });
  }

  /** يتحقق من تعبئة الحقول الإضافية الإلزامية. */
  private missingRequiredCustom(): FormField | null {
    return this.customFields().find(
      f => f.isRequired && !String(this.customValues[f.fieldKey] ?? '').trim()
    ) ?? null;
  }

  form = this.fb.nonNullable.group({
    title: ['', [Validators.required, Validators.minLength(6)]],
    implementationMechanism: ['', [Validators.required, Validators.minLength(20)]],
    submissionReasons: ['', [Validators.required, Validators.minLength(20)]],
  });

  get totalSize(): number {
    return this.selectedFiles().reduce((sum, f) => sum + f.size, 0);
  }

  get anyImpactSelected(): boolean {
    return this.impactFields().some(f => this.customValues[f.fieldKey] === 'true');
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

    const current = [...this.selectedFiles()];

    for (const file of Array.from(input.files)) {
      const ext = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
      if (!this.allowedExtensions.includes(ext)) {
        this.errorMessage.set(`نوع الملف "${file.name}" غير مسموح به.`);
        continue;
      }
      if (file.size > this.maxFileSizeBytes) {
        this.errorMessage.set(`حجم الملف "${file.name}" يتجاوز 10 ميجابايت.`);
        continue;
      }
      if (current.length >= this.maxFiles) {
        this.errorMessage.set(`الحد الأقصى ${this.maxFiles} ملفات.`);
        break;
      }
      const totalWithNew = current.reduce((s, f) => s + f.size, 0) + file.size;
      if (totalWithNew > this.maxTotalBytes) {
        this.errorMessage.set('الحجم الإجمالي للمرفقات يتجاوز 15 ميجابايت.');
        break;
      }
      if (!current.some(f => f.name === file.name && f.size === file.size)) current.push(file);
    }

    this.selectedFiles.set(current);
    input.value = '';
  }

  setCheckbox(key: string, checked: boolean) {
    this.customValues[key] = checked ? 'true' : 'false';
  }

  removeFile(file: File) {
    this.selectedFiles.update(list => list.filter(f => f !== file));
  }

  submit() {
    this.errorMessage.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.impactFields().length && !this.anyImpactSelected) {
      this.errorMessage.set('الرجاء تحديد أثر واحد على الأقل لتطبيق المقترح.');
      return;
    }
    const missing = this.missingRequiredCustom();
    if (missing) {
      this.errorMessage.set(`الحقل "${missing.labelAr}" إلزامي.`);
      return;
    }

    this.submitting.set(true);
    this.proposalService.create({
      ...this.form.getRawValue(),
      customFields: this.customValues,
    }).subscribe({
      next: created => {
        const files = this.selectedFiles();
        if (!files.length) {
          this.router.navigate(['/my-proposals']);
          return;
        }
        this.attachmentService.upload(created.id, files).subscribe({
          next: () => this.router.navigate(['/my-proposals']),
          error: () => {
            this.submitting.set(false);
            this.errorMessage.set('تم حفظ المقترح لكن تعذّر رفع بعض المرفقات. يمكنك إعادة رفعها لاحقاً من صفحة مقترحاتي.');
          },
        });
      },
      error: err => {
        this.submitting.set(false);
        // نعرض رسالة الخادم إن وُجدت، مع التفصيل التقني في بيئة التطوير للمساعدة في التشخيص
        const base = err?.error?.message ?? 'تعذّر إرسال المقترح.';
        const detail = err?.error?.inner ?? err?.error?.detail;
        this.errorMessage.set(detail ? `${base} (${detail})` : base);
      },
    });
  }
}
