import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { hm, hmin, uaeToday } from '../../core/format';
import { TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Day { date: string; locationName: string; scheduledStart: string; scheduledEnd: string; checkInAt: string | null; checkOutAt: string | null; status: string; netWorkMinutes: number; lateUnexcused: number; earlyUnexcused: number; overtimeMinutes: number; }
interface Month { year: number; month: number; scheduledDays: number; presentDays: number; absentDays: number; leaveDays: number; netWorkMinutes: number; lateMinutes: number; overtimeMinutes: number; days: Day[]; }

@Component({
  selector: 'app-my-attendance',
  standalone: true,
  imports: [FormsModule, TPipe],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'my.attendance' | t }}</h1><span class="spacer"></span>
      <input type="number" class="w-24" min="2020" max="2100" [(ngModel)]="year" (change)="load()">
      <input type="number" class="w-20" min="1" max="12" [(ngModel)]="month" (change)="load()">
    </div>

    @if (data(); as m) {
      <div class="grid gap-3 mb-4 [grid-template-columns:repeat(auto-fill,minmax(10rem,1fr))]">
        <div class="card-pad"><div class="text-xs text-muted">{{ 'my.presentDays' | t }}</div><div class="text-xl font-bold tabular">{{ m.presentDays }}/{{ m.scheduledDays }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'dash.absent' | t }}</div><div class="text-xl font-bold tabular" [class.text-bad]="m.absentDays > 0">{{ m.absentDays }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.late' | t }}</div><div class="text-xl font-bold tabular">{{ hmin(m.lateMinutes) }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.net' | t }}</div><div class="text-xl font-bold tabular">{{ hmin(m.netWorkMinutes) }}</div></div>
      </div>

      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.date' | t }}</th><th>{{ 'att.shift' | t }}</th><th>{{ 'att.in' | t }}</th><th>{{ 'att.out' | t }}</th>
          <th>{{ 'att.net' | t }}</th><th>{{ 'att.late' | t }}</th><th>{{ 'att.ot' | t }}</th><th>{{ 'common.status' | t }}</th></tr></thead>
        <tbody>
          @for (d of m.days; track d.date) {
            <tr [class.bg-bad-soft]="d.status === 'Absent'">
              <td dir="ltr" class="tabular">{{ d.date }}</td>
              <td dir="ltr" class="tabular">{{ hm(d.scheduledStart) }}–{{ hm(d.scheduledEnd) }}</td>
              <td class="tabular">{{ hm(d.checkInAt) }}</td><td class="tabular">{{ hm(d.checkOutAt) }}</td>
              <td class="tabular">{{ hmin(d.netWorkMinutes) }}</td>
              <td class="tabular" [class.text-bad]="d.lateUnexcused > 0">{{ hmin(d.lateUnexcused) }}</td>
              <td class="tabular">{{ hmin(d.overtimeMinutes) }}</td>
              <td><span class="badge" [class.green]="d.checkInAt" [class.red]="d.status === 'Absent'"
                        [class.blue]="d.status === 'OnLeave'">{{ 'state.' + d.status | t }}</span></td></tr>
          } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }`,
})
export class MyAttendancePage implements OnInit {
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly hmin = hmin;
  readonly data = signal<Month | null>(null);
  year = Number(uaeToday().substring(0, 4));
  month = Number(uaeToday().substring(5, 7));

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.data.set(await this.api.get<Month>('my/attendance', { year: this.year, month: this.month })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
