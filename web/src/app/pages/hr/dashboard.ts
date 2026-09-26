import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { hmin, uaeToday } from '../../core/format';
import { TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Row {
  id: string; shiftDate: string; employeeName: string; locationName: string; checkInAt: string | null;
  checkOutAt: string | null; status: string; netWorkMinutes: number; lateUnexcused: number; earlyUnexcused: number;
  overtimeMinutes: number;
}
interface Employee { id: string; fullName: string; departmentName: string; status: string; hireDate: string; }

/**
 * Office view of today plus the month to date, built from the same attendance records
 * as the field module — the numbers cannot disagree because there is only one source.
 */
@Component({
  selector: 'app-hr-dashboard',
  standalone: true,
  imports: [RouterLink, TPipe],
  template: `
    <div class="mb-6"><h1>{{ 'hr.nav.dashboard' | t }}</h1>
      <p class="mt-1 text-xs text-muted">{{ 'hr.dash.lead' | t }}</p></div>

    @if (loading()) {
      <div class="grid-cards">@for (i of [1,2,3,4,5,6]; track i) { <div class="skeleton h-[5.5rem]"></div> }</div>
    } @else {
      <div class="grid-cards">
        <a class="card stat" routerLink="/hr/employees"><div class="num">{{ activeEmployees() }}</div><div class="lbl">{{ 'hr.dash.staff' | t }}</div></a>
        <a class="card stat" routerLink="/hr/attendance"><div class="num text-ok">{{ presentToday() }}</div><div class="lbl">{{ 'dash.present' | t }}</div></a>
        <a class="card stat" routerLink="/hr/attendance"><div class="num text-warn">{{ lateToday() }}</div><div class="lbl">{{ 'dash.late' | t }}</div></a>
        <a class="card stat" routerLink="/hr/attendance"><div class="num text-bad">{{ absentToday() }}</div><div class="lbl">{{ 'dash.absent' | t }}</div></a>
        <a class="card stat" routerLink="/hr/attendance"><div class="num">{{ onLeaveToday() }}</div><div class="lbl">{{ 'dash.onLeave' | t }}</div></a>
        <div class="card stat"><div class="num">{{ attendanceRate() }}%</div><div class="lbl">{{ 'hr.dash.rate' | t }}</div></div>
      </div>

      <h2 class="mb-3">{{ 'hr.dash.month' | t }}</h2>
      <div class="grid gap-3 mb-6 [grid-template-columns:repeat(auto-fill,minmax(13rem,1fr))]">
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.net' | t }}</div><div class="text-xl font-bold tabular">{{ hmin(sum('netWorkMinutes')) }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.late' | t }}</div><div class="text-xl font-bold tabular text-bad">{{ hmin(sum('lateUnexcused')) }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.early' | t }}</div><div class="text-xl font-bold tabular">{{ hmin(sum('earlyUnexcused')) }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'att.ot' | t }}</div><div class="text-xl font-bold tabular">{{ hmin(sum('overtimeMinutes')) }}</div></div>
        <div class="card-pad"><div class="text-xs text-muted">{{ 'dash.absent' | t }}</div><div class="text-xl font-bold tabular">{{ absentDays() }}</div></div>
      </div>

      <h2 class="mb-3">{{ 'hr.dash.today' | t }}</h2>
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'att.location' | t }}</th><th>{{ 'att.in' | t }}</th>
          <th>{{ 'att.out' | t }}</th><th>{{ 'att.late' | t }}</th><th>{{ 'common.status' | t }}</th></tr></thead>
        <tbody>
          @for (r of today(); track r.id) {
            <tr><td>{{ r.employeeName }}</td><td>{{ r.locationName }}</td>
              <td class="tabular">{{ r.checkInAt ? r.checkInAt.substring(11,16) : '—' }}</td>
              <td class="tabular">{{ r.checkOutAt ? r.checkOutAt.substring(11,16) : '—' }}</td>
              <td class="tabular" [class.text-bad]="r.lateUnexcused > 0">{{ hmin(r.lateUnexcused) }}</td>
              <td><span class="badge" [class.green]="r.status === 'Present' || r.status === 'CheckedOut'"
                        [class.red]="r.status === 'Absent'" [class.blue]="r.status === 'OnLeave'">{{ 'state.' + r.status | t }}</span></td></tr>
          } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }`,
})
export class HrDashboardPage implements OnInit {
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hmin = hmin;
  readonly rows = signal<Row[]>([]);
  readonly employees = signal<Employee[]>([]);
  readonly loading = signal(true);
  private readonly branchIds = signal<string[]>([]);

  readonly today = computed(() => this.rows().filter(r => r.shiftDate === uaeToday()));
  readonly activeEmployees = computed(() => this.employees().filter(e => e.status === 'Active').length);
  readonly presentToday = computed(() => this.today().filter(r => r.checkInAt).length);
  readonly lateToday = computed(() => this.today().filter(r => r.lateUnexcused > 0).length);
  readonly absentToday = computed(() => this.today().filter(r => r.status === 'Absent').length);
  readonly onLeaveToday = computed(() => this.today().filter(r => r.status === 'OnLeave').length);
  readonly absentDays = computed(() => this.rows().filter(r => r.status === 'Absent').length);
  readonly attendanceRate = computed(() => {
    const expected = this.rows().filter(r => r.status !== 'OnLeave').length;
    if (!expected) return 100;
    const onTime = this.rows().filter(r => r.checkInAt && r.lateUnexcused === 0).length;
    return Math.round((onTime / expected) * 100);
  });

  async ngOnInit(): Promise<void> {
    try {
      const branches = await this.api.get<{ id: string }[]>('locations', { kind: 'Office' });
      this.branchIds.set(branches.map(b => b.id));

      const today = uaeToday();
      const monthStart = today.substring(0, 8) + '01';
      const [employees, ...batches] = await Promise.all([
        this.api.get<Employee[]>('hr/employees'),
        ...branches.map(b => this.api.get<Row[]>('attendance', { from: monthStart, to: today, locationId: b.id })),
      ]);
      this.employees.set(employees);
      this.rows.set(batches.flat());
    } catch (e) {
      this.ui.error(this.api.error(e).message);
    } finally {
      this.loading.set(false);
    }
  }

  sum(key: keyof Row): number { return this.rows().reduce((total, r) => total + (r[key] as number), 0); }
}
