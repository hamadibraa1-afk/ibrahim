import { Component, EventEmitter, Input, OnChanges, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProposalService } from '../../core/services/proposal.service';
import { FormField } from '../../core/models/form-field.model';
import {
  ImpactStatusBadgeClass, ImpactStatusLabels, MeasureImpactRequest, Proposal,
} from '../../core/models/proposal.model';

/** الحقول المخصّصة في قاعدة البيانات (أعمدة) وكيف تُربط بمفاتيح إعدادات النموذج. */
type DedicatedKey = keyof Omit<MeasureImpactRequest, 'customFields'>;
const DEDICATED: Record<string, DedicatedKey> = {
  actualAnnualSavings: 'actualAnnualSavings',
  actualAnnualRevenue: 'actualAnnualRevenue',
  implementationCost: 'implementationCost',
  hoursSavedPerMonth: 'hoursSavedPerMonth',
  beneficiariesReached: 'beneficiariesReached',
  satisfactionBefore: 'satisfactionBefore',
  satisfactionAfter: 'satisfactionAfter',
  targetAchievementPercent: 'targetAchievementPercent',
  impactRating: 'impactRating',
  impactSummary: 'summary',
  evidenceReference: 'evidenceReference',
};
const TEXT_KEYS: DedicatedKey[] = ['summary', 'evidenceReference'];

/**
 * مرحلة "قياس الأثر الفعلي" بعد الاعتماد بـ 3 إلى 6 أشهر: يسجّل مسؤول التنفيذ ما حققه
 * المقترح فعلاً (وفورات، إيرادات، تكلفة، رضا...) ويعتمده مدير النظام ليدخل تقارير العائد.
 * التسميات والإلزامية تأتي من إعدادات النموذج (قسم ImpactMeasurement).
 */
@Component({
  selector: 'app-impact-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  @if (proposal.impact; as impact) {
    <div class="card overflow-hidden border-2 border-cyan-100">
      <div class="bg-cyan-700 text-white px-5 py-2.5 text-[12.5px] font-bold flex items-center justify-between">
        <span>قياس الأثر الفعلي والعائد على الاستثمار</span>
        <span class="badge {{ ImpactStatusBadgeClass[impact.status] }}">{{ ImpactStatusLabels[impact.status] }}</span>
      </div>
      <div class="p-5">
        <div class="flex flex-wrap gap-x-6 gap-y-1 text-[12px] text-ink-soft mb-4">
          <span>تُفتح نافذة القياس: <b class="text-ink">{{ impact.opensAt | date:'d MMMM y' }}</b></span>
          <span>آخر موعد: <b class="text-ink">{{ impact.dueAt | date:'d MMMM y' }}</b></span>
          @if (impact.isOverdue) { <span class="badge badge-red">متأخر عن موعده</span> }
        </div>

        @if (impact.reviewNotes && impact.status === 'Open') {
          <div class="text-[12px] text-amber-800 bg-amber-50 border border-amber-100 rounded-xl px-3.5 py-2.5 mb-4">
            ملاحظات المراجعة: {{ impact.reviewNotes }}
          </div>
        }

        @if (impact.measuredAt) {
          <div class="grid sm:grid-cols-3 gap-3 mb-4">
            <div class="bg-[#F6FAF8] rounded-xl p-3">
              <div class="text-[10.5px] text-ink-faint">صافي المنفعة السنوية</div>
              <div class="text-[16px] font-extrabold text-brand-700">{{ (impact.totalAnnualBenefit ?? 0) | number:'1.0-0' }} <span class="text-[10px]">درهم</span></div>
            </div>
            <div class="bg-[#F6FAF8] rounded-xl p-3">
              <div class="text-[10.5px] text-ink-faint">تكلفة التطبيق</div>
              <div class="text-[16px] font-extrabold text-ink">{{ (impact.implementationCost ?? 0) | number:'1.0-0' }} <span class="text-[10px]">درهم</span></div>
            </div>
            <div class="bg-[#F6FAF8] rounded-xl p-3">
              <div class="text-[10.5px] text-ink-faint">العائد على الاستثمار (ROI)</div>
              <div class="text-[16px] font-extrabold" [class.text-emerald-700]="(impact.roiPercent ?? 0) >= 0" [class.text-red-700]="(impact.roiPercent ?? 0) < 0">
                {{ impact.roiPercent !== null ? (impact.roiPercent + '%') : '—' }}
              </div>
            </div>
          </div>
          <div class="text-[11px] text-ink-faint mb-4">
            سجّله {{ impact.measuredByName }} · {{ impact.measuredAt | date:'d MMMM y' }}
            @if (impact.verifiedAt) { · اعتمده {{ impact.verifiedByName }} · {{ impact.verifiedAt | date:'d MMMM y' }} }
          </div>
        }

        @if (proposal.actions.canMeasureImpact) {
          <div class="grid sm:grid-cols-2 gap-3.5">
            @for (f of dedicatedNumberFields(); track f.id) {
              <div>
                <label class="field-label">{{ f.labelAr }} @if (f.isRequired) { * }</label>
                <input type="number" min="0" step="any" class="field-input" dir="ltr"
                       [placeholder]="f.placeholder ?? ''"
                       [ngModel]="model[key(f)]" (ngModelChange)="setNumber(f, $event)">
              </div>
            }
          </div>
          @for (f of dedicatedTextFields(); track f.id) {
            <div class="mt-3.5">
              <label class="field-label">{{ f.labelAr }} @if (f.isRequired) { * }</label>
              @if (key(f) === 'summary') {
                <textarea rows="3" class="field-input" [(ngModel)]="model.summary" [placeholder]="f.placeholder ?? ''"></textarea>
              } @else {
                <input type="text" class="field-input" [(ngModel)]="model.evidenceReference" [placeholder]="f.placeholder ?? ''">
              }
            </div>
          }
          @for (f of customFields(); track f.id) {
            <div class="mt-3.5">
              <label class="field-label">{{ f.labelAr }} @if (f.isRequired) { * }</label>
              @switch (f.fieldType) {
                @case ('TextArea') { <textarea rows="2" class="field-input" [(ngModel)]="custom[f.fieldKey]"></textarea> }
                @case ('Number') { <input type="number" class="field-input" dir="ltr" [(ngModel)]="custom[f.fieldKey]"> }
                @case ('Date') { <input type="date" class="field-input" [(ngModel)]="custom[f.fieldKey]"> }
                @case ('Checkbox') {
                  <label class="flex items-center gap-2"><input type="checkbox" class="w-4 h-4 accent-brand-600"
                    [checked]="custom[f.fieldKey] === 'true'" (change)="custom[f.fieldKey] = $any($event.target).checked ? 'true' : 'false'"> نعم</label>
                }
                @case ('Select') {
                  <select class="field-input" [(ngModel)]="custom[f.fieldKey]">
                    <option value="">— اختر —</option>
                    @for (o of f.options; track o) { <option [value]="o">{{ o }}</option> }
                  </select>
                }
                @default { <input type="text" class="field-input" [(ngModel)]="custom[f.fieldKey]"> }
              }
            </div>
          }
          <div class="mt-4">
            <button (click)="save()" [disabled]="busy()" class="btn btn-primary">
              {{ impact.status === 'Submitted' ? 'تحديث القياس' : 'حفظ قياس الأثر وإرساله للاعتماد' }}
            </button>
          </div>
        } @else if (!impact.measuredAt) {
          <p class="text-[12px] text-ink-faint">
            @if (impact.status === 'Scheduled') {
              يُتاح التسجيل لمسؤول التنفيذ عند فتح نافذة القياس بعد 3 أشهر من الاعتماد.
            } @else { بانتظار مسؤول التنفيذ لتسجيل القياس. }
          </p>
        }

        @if (proposal.actions.canVerifyImpact) {
          <div class="mt-5 pt-4 border-t border-[#EEF1EF]">
            <label class="field-label">ملاحظات الاعتماد (إلزامية عند الإعادة للتصحيح)</label>
            <textarea rows="2" class="field-input mb-3" [(ngModel)]="reviewNotes"></textarea>
            <div class="flex gap-2.5">
              <button (click)="review(true)" [disabled]="busy()" class="btn btn-primary">اعتماد القياس</button>
              <button (click)="review(false)" [disabled]="busy()" class="btn btn-ghost">إعادة للتصحيح</button>
            </div>
          </div>
        }

        @if (error()) {
          <div class="mt-3 text-[12.5px] text-red-700 bg-red-50 border border-red-100 rounded-xl px-4 py-3">{{ error() }}</div>
        }
      </div>
    </div>
  }
  `,
})
export class ImpactPanelComponent implements OnChanges {
  private proposals = inject(ProposalService);

  @Input({ required: true }) proposal!: Proposal;
  /** حقول قسم "قياس الأثر الفعلي" المفعّلة من إعدادات النموذج. */
  @Input() fields: FormField[] = [];
  @Output() updated = new EventEmitter<Proposal>();

  readonly ImpactStatusLabels = ImpactStatusLabels;
  readonly ImpactStatusBadgeClass = ImpactStatusBadgeClass;

  model: Omit<MeasureImpactRequest, 'customFields'> = this.empty();
  custom: Record<string, string> = {};
  reviewNotes = '';
  busy = signal(false);
  error = signal<string | null>(null);

  ngOnChanges() {
    const i = this.proposal?.impact;
    if (!i) return;
    this.model = {
      actualAnnualSavings: i.actualAnnualSavings, actualAnnualRevenue: i.actualAnnualRevenue,
      implementationCost: i.implementationCost, hoursSavedPerMonth: i.hoursSavedPerMonth,
      beneficiariesReached: i.beneficiariesReached, satisfactionBefore: i.satisfactionBefore,
      satisfactionAfter: i.satisfactionAfter, targetAchievementPercent: i.targetAchievementPercent,
      impactRating: i.impactRating, summary: i.summary, evidenceReference: i.evidenceReference,
    };
    this.custom = {};
    i.customFields.forEach(cf => (this.custom[cf.fieldKey] = cf.value ?? ''));
  }

  key(f: FormField): DedicatedKey { return DEDICATED[f.fieldKey]; }

  dedicatedNumberFields(): FormField[] {
    return this.sorted(this.fields.filter(f => f.isSystem && DEDICATED[f.fieldKey] && !TEXT_KEYS.includes(DEDICATED[f.fieldKey])));
  }
  dedicatedTextFields(): FormField[] {
    return this.sorted(this.fields.filter(f => f.isSystem && DEDICATED[f.fieldKey] && TEXT_KEYS.includes(DEDICATED[f.fieldKey])));
  }
  customFields(): FormField[] {
    return this.sorted(this.fields.filter(f => !f.isSystem));
  }

  setNumber(f: FormField, value: number | string | null) {
    const n = value === '' || value === null ? null : Number(value);
    (this.model as Record<string, unknown>)[this.key(f)] = n !== null && Number.isFinite(n) ? n : null;
  }

  save() {
    this.error.set(null);
    const missing = this.fields.find(f => f.isRequired && (
      DEDICATED[f.fieldKey]
        ? this.isEmpty(this.model[DEDICATED[f.fieldKey]])
        : !f.isSystem && f.fieldType !== 'Checkbox' && !String(this.custom[f.fieldKey] ?? '').trim()));
    if (missing) { this.error.set(`الحقل "${missing.labelAr}" إلزامي.`); return; }

    this.busy.set(true);
    this.proposals.measureImpact(this.proposal.id, { ...this.model, customFields: this.custom }).subscribe({
      next: p => { this.busy.set(false); this.updated.emit(p); },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'تعذّر حفظ القياس.'); },
    });
  }

  review(approve: boolean) {
    this.error.set(null);
    if (!approve && !this.reviewNotes.trim()) { this.error.set('الرجاء توضيح ما يلزم تصحيحه.'); return; }
    this.busy.set(true);
    this.proposals.reviewImpact(this.proposal.id, { approve, notes: this.reviewNotes || undefined }).subscribe({
      next: p => { this.busy.set(false); this.reviewNotes = ''; this.updated.emit(p); },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'تعذّر تنفيذ الإجراء.'); },
    });
  }

  private isEmpty(v: unknown): boolean {
    return v === null || v === undefined || (typeof v === 'string' && !v.trim());
  }

  private sorted(list: FormField[]): FormField[] {
    return [...list].sort((a, b) => a.sortOrder - b.sortOrder);
  }

  private empty(): Omit<MeasureImpactRequest, 'customFields'> {
    return {
      actualAnnualSavings: null, actualAnnualRevenue: null, implementationCost: null, hoursSavedPerMonth: null,
      beneficiariesReached: null, satisfactionBefore: null, satisfactionAfter: null,
      targetAchievementPercent: null, impactRating: null, summary: null, evidenceReference: null,
    };
  }
}
