import { Component, OnInit, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { dayIndex } from '../../core/format';

interface ScheduleDay { day: string | number; startTime: string; endTime: string; breakMinutes: number; }
interface Profile {
  fullName: string; employeeNumber: string; email: string | null; phone: string; role: string;
  jobTitle: string | null; department: string | null; section: string | null; branch: string | null;
  manager: string | null; scheduleName: string | null; hireDate: string | null; status: string | null;
  nationality: string | null; idNumber: string | null; idExpiry: string | null;
  passportNumber: string | null; passportExpiry: string | null; iban: string | null; scheduleDays: ScheduleDay[];
}

@Component({
  selector: 'app-my-profile',
  standalone: true,
  imports: [TPipe],
  template: `
    <h1 class="mb-4">{{ 'my.profile' | t }}</h1>
    @if (data(); as p) {
      <section class="card-pad mb-4">
        <h2 class="mb-3">{{ 'my.personal' | t }}</h2>
        <dl class="grid grid-cols-[auto_1fr] gap-x-5 gap-y-2.5 text-sm">
          <dt class="text-muted">{{ 'common.name' | t }}</dt><dd>{{ p.fullName }}</dd>
          <dt class="text-muted">{{ 'emp.number' | t }}</dt><dd dir="ltr">{{ p.employeeNumber }}</dd>
          <dt class="text-muted">{{ 'emp.phone' | t }}</dt><dd dir="ltr">{{ p.phone }}</dd>
          <dt class="text-muted">{{ 'emp.email' | t }}</dt><dd dir="ltr">{{ p.email ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.emp.nationality' | t }}</dt><dd>{{ p.nationality ?? '—' }}</dd>
        </dl>
      </section>

      <section class="card-pad mb-4">
        <h2 class="mb-3">{{ 'my.job' | t }}</h2>
        <dl class="grid grid-cols-[auto_1fr] gap-x-5 gap-y-2.5 text-sm">
          <dt class="text-muted">{{ 'hr.emp.jobTitle' | t }}</dt><dd>{{ p.jobTitle ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.org.department' | t }}</dt><dd>{{ p.department ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.org.section' | t }}</dt><dd>{{ p.section ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.emp.branch' | t }}</dt><dd>{{ p.branch ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.emp.manager' | t }}</dt><dd>{{ p.manager ?? '—' }}</dd>
          <dt class="text-muted">{{ 'hr.emp.hireDate' | t }}</dt><dd dir="ltr">{{ p.hireDate ?? '—' }}</dd>
        </dl>
      </section>

      <section class="card-pad mb-4">
        <h2 class="mb-3">{{ 'my.schedule' | t }} <span class="muted small">{{ p.scheduleName }}</span></h2>
        <div class="divide-y divide-line">
          @for (d of p.scheduleDays; track d.day) {
            <div class="flex items-center gap-3 py-2 text-sm">
              <span class="w-24 font-semibold">{{ ('day.' + dayIndex(d.day)) | t }}</span>
              <span class="tabular" dir="ltr">{{ d.startTime.substring(0,5) }}–{{ d.endTime.substring(0,5) }}</span>
            </div>
          } @empty { <p class="py-2 text-muted">{{ 'common.empty' | t }}</p> }
        </div>
      </section>

      <section class="card-pad">
        <h2 class="mb-3">{{ 'my.documents' | t }}</h2>
        <dl class="grid grid-cols-[auto_1fr] gap-x-5 gap-y-2.5 text-sm">
          <dt class="text-muted">{{ 'hr.emp.id' | t }}</dt><dd dir="ltr">{{ p.idNumber ?? '—' }} <span class="muted">{{ p.idExpiry }}</span></dd>
          <dt class="text-muted">{{ 'hr.emp.passport' | t }}</dt><dd dir="ltr">{{ p.passportNumber ?? '—' }} <span class="muted">{{ p.passportExpiry }}</span></dd>
          <dt class="text-muted">{{ 'hr.emp.iban' | t }}</dt><dd dir="ltr">{{ p.iban ?? '—' }}</dd>
        </dl>
        <p class="mt-3 text-xs text-muted">{{ 'my.updateHint' | t }}</p>
      </section>
    }`,
})
export class MyProfilePage implements OnInit {
  readonly dayIndex = dayIndex;
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly data = signal<Profile | null>(null);

  async ngOnInit(): Promise<void> {
    try { this.data.set(await this.api.get<Profile>('my/profile')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
