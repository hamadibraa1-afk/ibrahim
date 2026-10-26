import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { PlanContact, SafetyPlanSection, SafetyPlanVersion, SafetyPlanVersionSummary, StaffSafetyPlan } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { careUi } from './care-dialogs';
import { PatientTabsComponent } from './patient-tabs.component';

/** Steps 1–6 and the reason to live, in the order of the patient's P37. */
export const SAFETY_SECTIONS: readonly SafetyPlanSection[] = ['warningSigns', 'copingAlone', 'distractions', 'peopleToAsk', 'professionals', 'safeEnvironment', 'reasonToLive'];
export const MAX_CONTACTS = 5;
/** Saudi mobile or landline digits, with an optional + (the API checks the number too). */
const PHONE = /^\+?[0-9 ]{7,16}$/;

/**
 * S35 safety-plan builder (/patients/:id/safety-plan). The therapist writes the plan with the patient, in the patient's
 * words: the six steps and a reason to live, and people to ask for help (name and phone, as in P37). Step 5 adds the
 * therapist, the support line and the local emergency number from the platform settings. "Save and send" stores a new
 * version that the patient's app keeps on the device; the patient can still edit it in P37. Both share one version
 * number: the save sends the version that was read (If-Match), and when the patient saved meanwhile (412) the two
 * versions are shown side by side. Earlier versions open read-only.
 */
@Component({
  selector: 'tm-safety-plan-builder',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, TranslateModule, LocalDatePipe, ProblemMessageComponent, PatientTabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './safety-plan-builder.component.html',
  styleUrl: './care.scss',
})
export class SafetyPlanBuilderComponent {
  readonly id = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  protected readonly language = inject(LanguageService);

  protected readonly sections = SAFETY_SECTIONS;
  protected readonly maxContacts = MAX_CONTACTS;
  protected readonly plan = signal<StaffSafetyPlan | null>(null);
  protected readonly versions = signal<SafetyPlanVersionSummary[]>([]);
  protected readonly viewing = signal<SafetyPlanVersion | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly saveError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);
  protected readonly preview = signal(false);
  protected readonly conflict = signal<StaffSafetyPlan | null>(null);
  protected readonly addingContact = signal(false);
  protected draft: Record<SafetyPlanSection, string> = this.empty();
  protected contacts: PlanContact[] = [];
  protected newName = '';
  protected newPhone = '';
  private version = 0;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.api
      .safetyPlan(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => {
          this.apply(p);
          this.loading.set(false);
          this.api.safetyPlanVersions(id).subscribe({ next: (v) => this.versions.set(v), error: () => this.versions.set([]) });
        },
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.safety.loadError'));
          }
        },
      });
  }

  protected validPhone(): boolean {
    return PHONE.test(this.newPhone.trim());
  }

  protected addContact(): void {
    const name = this.newName.trim();
    if (!name || name.length > 100 || !this.validPhone() || this.contacts.length >= MAX_CONTACTS) {
      return;
    }
    this.contacts = [...this.contacts, { name, phone: this.newPhone.replace(/\s/g, '') }];
    this.newName = '';
    this.newPhone = '';
    this.addingContact.set(false);
  }

  protected removeContact(i: number): void {
    this.contacts = this.contacts.filter((_, j) => j !== i);
  }

  protected tooLong(): boolean {
    return SAFETY_SECTIONS.some((s) => (this.draft[s] ?? '').length > 1000);
  }

  protected save(): void {
    const p = this.plan();
    if (!p || !p.canEdit || this.saving() || this.tooLong()) {
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.api.saveSafetyPlan(p.pseudonymId, this.version, { sections: this.draft, contacts: this.contacts }).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.conflict.set(null);
        this.apply(saved);
        this.ui.notify('care.safety.saved');
        this.api.safetyPlanVersions(saved.pseudonymId).subscribe({ next: (v) => this.versions.set(v) });
      },
      error: (e: unknown) => {
        // "Save failed": the text stays; a 412 shows the patient's newer version next to this draft.
        this.saving.set(false);
        if (statusOf(e) === 412) {
          this.api.safetyPlan(p.pseudonymId).subscribe((latest) => {
            this.conflict.set(latest);
            this.version = latest.version;
          });
        } else {
          this.saveError.set(shownError(e, 'care.safety.saveError'));
        }
      },
    });
  }

  protected useTheirs(): void {
    const c = this.conflict();
    if (c) {
      this.apply(c);
      this.conflict.set(null);
    }
  }

  protected view(version: number): void {
    const p = this.plan();
    if (p) {
      this.api.safetyPlanVersion(p.pseudonymId, version).subscribe({ next: (v) => this.viewing.set(v), error: (e: unknown) => this.saveError.set(shownError(e, 'common.error')) });
    }
  }

  private apply(p: StaffSafetyPlan): void {
    this.plan.set(p);
    this.version = p.version;
    this.draft = { ...this.empty(), ...p.sections };
    this.contacts = [...p.contacts];
  }

  private empty(): Record<SafetyPlanSection, string> {
    return Object.fromEntries(SAFETY_SECTIONS.map((s) => [s, ''])) as Record<SafetyPlanSection, string>;
  }
}
