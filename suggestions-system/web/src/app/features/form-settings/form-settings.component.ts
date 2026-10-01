import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormSettingsService } from '../../core/services/form-settings.service';
import {
  FieldTypeLabels, FormField, FormFieldType, SectionLabels,
} from '../../core/models/form-field.model';

/**
 * إعدادات حقول النموذج — تتيح لمدير النظام إضافة/تعديل/إخفاء حقول
 * نموذج المقترح دون العودة للبرمجة.
 */
@Component({
  selector: 'app-form-settings',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule],
  templateUrl: './form-settings.component.html',
})
export class FormSettingsComponent implements OnInit {
  private fb = inject(FormBuilder);
  private service = inject(FormSettingsService);

  readonly FieldTypeLabels = FieldTypeLabels;
  readonly SectionLabels = SectionLabels;
  readonly typeOptions = Object.keys(FieldTypeLabels) as FormFieldType[];
  readonly sectionOptions = Object.keys(SectionLabels);

  fields = signal<FormField[]>([]);
  loading = signal(true);
  saving = signal(false);
  showModal = signal(false);
  editing = signal<FormField | null>(null);
  errorMessage = signal<string | null>(null);
  optionsText = '';

  form = this.fb.nonNullable.group({
    fieldKey: ['', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]*$/)]],
    labelAr: ['', Validators.required],
    labelEn: [''],
    placeholder: [''],
    fieldType: ['Text' as FormFieldType, Validators.required],
    section: ['SuggestionDetails', Validators.required],
    isRequired: [false],
    isActive: [true],
    sortOrder: [0],
  });

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: d => { this.fields.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  /** يولّد مفتاحاً إنجليزياً فريداً تلقائياً حتى لا يضطر المستخدم لابتكاره. */
  private generateKey(section: string): string {
    const base = section === 'Impact' ? 'impact' : section === 'SuggestionData' ? 'data' : 'field';
    const used = new Set(this.fields().map(f => f.fieldKey.toLowerCase()));
    let n = 1;
    while (used.has(`${base}Custom${n}`.toLowerCase())) n++;
    return `${base}Custom${n}`;
  }

  openAdd(section: string = 'SuggestionDetails') {
    this.editing.set(null);
    this.optionsText = '';
    this.errorMessage.set(null);

    // في قسم "أثر التطبيق" النوع دائماً خانة اختيار — نضبطه مسبقاً لتبسيط الإضافة
    const type: FormFieldType = section === 'Impact' ? 'Checkbox' : 'Text';
    const inSection = this.fieldsInSection(section);
    const nextOrder = inSection.length
      ? Math.max(...inSection.map(f => f.sortOrder)) + 1
      : this.fields().length;

    this.form.reset({
      fieldKey: this.generateKey(section), labelAr: '', labelEn: '', placeholder: '',
      fieldType: type, section,
      isRequired: false, isActive: true, sortOrder: nextOrder,
    });
    this.form.controls.fieldKey.enable();
    this.showModal.set(true);
  }

  /** زر الإضافة داخل كل قسم — يفتح النافذة مضبوطة على ذلك القسم مباشرة. */
  addToSection(section: string) { this.openAdd(section); }

  /** عنوان النافذة يوضّح القسم الذي ستُضاف إليه. */
  get modalTitle(): string {
    if (this.editing()) return 'تعديل حقل';
    const sec = this.form.controls.section.value;
    return `إضافة حقل إلى: ${this.SectionLabels[sec] ?? sec}`;
  }

  openEdit(f: FormField) {
    this.editing.set(f);
    this.optionsText = f.options.join('\n');
    this.errorMessage.set(null);
    this.form.reset({
      fieldKey: f.fieldKey, labelAr: f.labelAr, labelEn: f.labelEn ?? '',
      placeholder: f.placeholder ?? '', fieldType: f.fieldType, section: f.section,
      isRequired: f.isRequired, isActive: f.isActive, sortOrder: f.sortOrder,
    });
    // مفتاح الحقل لا يُعدَّل بعد الإنشاء حفاظاً على ارتباط البيانات السابقة
    this.form.controls.fieldKey.disable();
    this.showModal.set(true);
  }

  close() { this.showModal.set(false); this.editing.set(null); }

  save() {
    this.errorMessage.set(null);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }

    const v = this.form.getRawValue();
    const payload = {
      ...v,
      options: v.fieldType === 'Select'
        ? this.optionsText.split('\n').map(o => o.trim()).filter(Boolean)
        : [],
    };

    if (payload.fieldType === 'Select' && payload.options.length < 2) {
      this.errorMessage.set('القائمة المنسدلة تحتاج خيارين على الأقل.');
      return;
    }

    this.saving.set(true);
    const editing = this.editing();
    const req$ = editing ? this.service.update(editing.id, payload) : this.service.create(payload);

    req$.subscribe({
      next: () => { this.saving.set(false); this.close(); this.load(); },
      error: err => { this.saving.set(false); this.errorMessage.set(err?.error?.message ?? 'تعذّر الحفظ.'); },
    });
  }

  toggleActive(f: FormField) {
    this.service.update(f.id, { ...f, isActive: !f.isActive, options: f.options }).subscribe({
      next: () => this.load(),
      error: err => alert(err?.error?.message ?? 'تعذّر التحديث.'),
    });
  }

  move(f: FormField, dir: -1 | 1) {
    const list = [...this.fields()].sort((a, b) => a.sortOrder - b.sortOrder);
    const i = list.findIndex(x => x.id === f.id);
    const j = i + dir;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    this.service.reorder(list.map(x => x.id)).subscribe(() => this.load());
  }

  remove(f: FormField) {
    if (f.isSystem) return;
    if (!confirm(`حذف الحقل "${f.labelAr}"؟`)) return;
    this.service.delete(f.id).subscribe({
      next: () => this.load(),
      error: err => alert(err?.error?.message ?? 'تعذّر الحذف.'),
    });
  }

  fieldsInSection(section: string): FormField[] {
    return this.fields().filter(f => f.section === section).sort((a, b) => a.sortOrder - b.sortOrder);
  }
}
