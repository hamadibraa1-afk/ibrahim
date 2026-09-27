import { Component, OnInit, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { downloadCsv, hm, hmin, uaeToday, weekStart, addDays } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Backdrop } from '../../core/backdrop';

interface Exit { exitAt: string; returnAt: string | null; permissionEnd: string; }
interface Row { id: string; shiftDate: string; employeeId: string; employeeName: string; employeeNumber: string | null; locationName: string; shiftName: string; scheduledStart: string; scheduledEnd: string; checkInAt: string | null; checkInType: string | null; checkInDistance: number | null; checkOutAt: string | null; checkOutType: string | null; status: string; netWorkMinutes: number; permissionMinutes: number; lateExcused: number; lateUnexcused: number; earlyExcused: number; earlyUnexcused: number; overtimeMinutes: number; exits: Exit[]; allowances: string[]; }
interface Named { id: string; nameAr?: string; nameEn?: string; fullName?: string; }

@Component({
  selector: 'app-attendance',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  styles: [`
    .chips { display: flex; gap: 6px; flex-wrap: wrap; align-items: center; margin-top: 10px; border-top: 1px solid var(--border); padding-top: 10px; }
    .chip-btn { padding: 5px 12px; border-radius: 999px; border: 1px solid var(--border); background: var(--surface); cursor: pointer; font-size: .85rem; }
    .chip-btn.on { background: var(--primary); color: var(--primary-ink); border-color: var(--primary); }
    .chip-btn.clear { color: var(--red); border-color: var(--red); }
  `],
  template: `
    <h1>{{ 'att.title' | t }}</h1>
    <div class="card" style="margin-bottom:14px"><div class="row">
      <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
      <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
      <div class="field"><label>{{ 'att.employee' | t }}</label><select [(ngModel)]="employeeId"><option value="">{{ 'common.all' | t }}</option>
        @for (e of employees(); track e.id) { <option [value]="e.id">{{ e.fullName }}</option> }</select></div>
      @if (!workforce()) {
        <div class="field"><label for="att-wf">{{ 'wf.title' | t }}</label>
          <select id="att-wf" [(ngModel)]="side" (change)="load()"><option value="">{{ 'common.all' | t }}</option>
            <option value="Office">{{ 'wf.Office' | t }}</option><option value="Field">{{ 'wf.Field' | t }}</option></select></div>
      }
      <div class="field"><label>{{ 'att.location' | t }}</label><select [(ngModel)]="locationId"><option value="">{{ 'common.all' | t }}</option>
        @for (l of locations(); track l.id) { <option [value]="l.id">{{ i18n.pick(l.nameAr!, l.nameEn!) }}</option> }</select></div>
      <button class="btn primary" style="margin-bottom:12px" (click)="load()">{{ 'common.search' | t }}</button>
      <button class="btn" style="margin-bottom:12px" (click)="export()">{{ 'common.export' | t }}</button>
    </div>
    <div class="chips">
      @for (c of chips; track c.key) {
        <button class="chip-btn" [class.on]="status === c.key" (click)="setFilter(c.key)">{{ c.label | t }}</button>
      }
      @if (status) { <button class="chip-btn clear" (click)="setFilter('')">✕ {{ 'att.clearFilter' | t }}</button> }
      <span class="spacer" style="flex:1"></span>
      <span class="muted small">{{ 'att.rows' | t }}: {{ filtered().length }}</span>
    </div></div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'common.date' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'att.location' | t }}</th><th>{{ 'att.shift' | t }}</th>
        <th>{{ 'att.in' | t }}</th><th>{{ 'att.out' | t }}</th><th>{{ 'att.net' | t }}</th><th>{{ 'att.late' | t }}</th><th>{{ 'att.lateEx' | t }}</th>
        <th>{{ 'att.early' | t }}</th><th>{{ 'att.earlyEx' | t }}</th><th>{{ 'att.perm' | t }}</th><th>{{ 'att.ot' | t }}</th><th>{{ 'allow.title' | t }}</th><th>{{ 'common.status' | t }}</th><th></th></tr></thead>
      <tbody>
        @for (r of filtered(); track r.id) {
          <tr>
            <td dir="ltr">{{ r.shiftDate }}</td><td>{{ r.employeeName }}</td><td>{{ r.locationName }}</td>
            <td dir="ltr">{{ hm(r.scheduledStart) }}–{{ hm(r.scheduledEnd) }}</td>
            <td>{{ hm(r.checkInAt) }} @if (r.checkInType === 'Exception') { <span class="badge yellow">{{ 'att.exception' | t }}</span> }</td>
            <td>{{ hm(r.checkOutAt) }} @if (r.checkOutType === 'Auto') { <span class="badge">{{ 'att.auto' | t }}</span> }
              @if (r.checkOutType === 'UnreturnedExit') { <span class="badge red">{{ 'att.unreturned' | t }}</span> }
              @if (r.checkOutType === 'Exception') { <span class="badge yellow">{{ 'att.exception' | t }}</span> }</td>
            <td>{{ hmin(r.netWorkMinutes) }}</td>
            <td [style.color]="r.lateUnexcused ? 'var(--red)' : ''">{{ hmin(r.lateUnexcused) }}</td><td>{{ hmin(r.lateExcused) }}</td>
            <td [style.color]="r.earlyUnexcused ? 'var(--red)' : ''">{{ hmin(r.earlyUnexcused) }}</td><td>{{ hmin(r.earlyExcused) }}</td>
            <td>{{ hmin(r.permissionMinutes) }}</td><td>{{ hmin(r.overtimeMinutes) }}</td>
            <td>@for (a of r.allowances; track a) { <span class="badge blue">{{ a }}</span> }</td>
            <td><span class="badge {{ badge(r.status) }}">{{ 'state.' + r.status | t }}</span></td>
            <td><button class="btn sm" (click)="detail.set(r)">{{ 'common.details' | t }}</button></td>
          </tr>
        } @empty { <tr><td colspan="16" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody>
      @if (filtered().length) {
        <tfoot><tr><td colspan="6">{{ 'common.total' | t }} ({{ filtered().length }})</td>
          <td>{{ hmin(sum('netWorkMinutes')) }}</td><td>{{ hmin(sum('lateUnexcused')) }}</td><td>{{ hmin(sum('lateExcused')) }}</td>
          <td>{{ hmin(sum('earlyUnexcused')) }}</td><td>{{ hmin(sum('earlyExcused')) }}</td><td>{{ hmin(sum('permissionMinutes')) }}</td>
          <td>{{ hmin(sum('overtimeMinutes')) }}</td><td colspan="3"></td></tr></tfoot>
      }
    </table></div>

    @if (detail(); as r) {
      <div class="modal-back" appBackdrop (dismiss)="detail.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ r.employeeName }} · <span dir="ltr">{{ r.shiftDate }}</span></h2><button class="btn sm" (click)="detail.set(null)">{{ 'common.close' | t }}</button></div>
        <h2>{{ 'att.timeline' | t }}</h2>
        <ul>
          <li>{{ 'att.shift' | t }}: <span dir="ltr">{{ hm(r.scheduledStart) }}–{{ hm(r.scheduledEnd) }}</span> ({{ r.locationName }})</li>
          <li>{{ 'att.in' | t }}: {{ hm(r.checkInAt) }} @if (r.checkInDistance !== null) { · {{ 'att.distance' | t }}: {{ r.checkInDistance }} m }</li>
          @for (x of r.exits; track x.exitAt) {
            <li>{{ 'att.exitOut' | t }}: {{ hm(x.exitAt) }} → {{ 'att.exitBack' | t }}: {{ hm(x.returnAt) }} ({{ 'perm.until' | t }} {{ hm(x.permissionEnd) }})</li>
          }
          <li>{{ 'att.out' | t }}: {{ hm(r.checkOutAt) }} {{ r.checkOutType ? '(' + r.checkOutType + ')' : '' }}</li>
        </ul>
      </div></div>
    }`,
})
export class AttendancePage implements OnInit {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly hmin = hmin;
  from = weekStart(uaeToday());
  to = addDays(this.from, 6);
  employeeId = '';
  locationId = '';
  status = '';
  readonly rows = signal<Row[]>([]);
  readonly employees = signal<Named[]>([]);
  readonly locations = signal<Named[]>([]);
  readonly detail = signal<Row | null>(null);
  readonly view = input<string>();
  /**
   * Set by the route. The field module fixes it to Field, so office staff never appear there;
   * HR leaves it empty and sees everyone, with a filter to narrow to one workforce.
   */
  readonly workforce = input<string>('');
  side = '';
  private readonly statusFilter = signal('');
  readonly chips = [
    { key: 'present', label: 'dash.present' }, { key: 'late', label: 'att.filterLate' }, { key: 'Absent', label: 'dash.absent' },
    { key: 'OnLeave', label: 'dash.onLeave' }, { key: 'exit', label: 'dash.onExit' }, { key: 'early', label: 'att.filterEarly' },
    { key: 'exception', label: 'att.filterException' }, { key: 'auto', label: 'att.filterAuto' },
  ];

  constructor() {
    // Cards on the dashboard link here with a view, so the list opens already narrowed to that group.
    effect(() => {
      const view = this.view();
      if (!view) return;
      const map: Record<string, string> = { today: '', present: 'present', late: 'late', absent: 'Absent', leave: 'OnLeave', exit: 'exit' };
      this.status = map[view] ?? '';
      this.from = uaeToday();
      this.to = uaeToday();
      this.load();
    });
  }

  readonly filtered = computed(() => {
    const s = this.statusFilter();
    return this.rows().filter(r =>
      !s ? true
        : s === 'late' ? r.lateUnexcused > 0 || (r.status === 'Scheduled' && !r.checkInAt)
        : s === 'present' ? !!r.checkInAt && !r.checkOutAt
        : s === 'exit' ? r.exits.some(x => !x.returnAt)
        : s === 'early' ? r.earlyUnexcused > 0
        : s === 'exception' ? r.checkInType === 'Exception' || r.checkOutType === 'Exception'
        : s === 'auto' ? r.checkOutType === 'Auto' || r.checkOutType === 'UnreturnedExit' : r.status === s);
  });

  setFilter(key: string): void { this.status = key; this.statusFilter.set(key); }

  async ngOnInit(): Promise<void> {
    const field = this.workforce() === 'Field';
    const [e, l] = await Promise.all([
      this.api.get<Named[]>('employees', field ? { role: 'Collector' } : {}),
      this.api.get<Named[]>('locations', field ? { kind: 'Field' } : {}),
    ]);
    this.employees.set(e);
    this.locations.set(l);
    await this.load();
  }

  async load(): Promise<void> {
    try {
      this.rows.set(await this.api.get<Row[]>('attendance', {
        from: this.from, to: this.to, employeeId: this.employeeId, locationId: this.locationId,
        workforce: this.workforce() || this.side,
      }));
      this.statusFilter.set(this.status);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  sum(k: keyof Row): number { return this.filtered().reduce((a, r) => a + (r[k] as number), 0); }

  badge(s: string): string { return ({ Present: 'green', CheckedOut: 'green', Absent: 'red', OnLeave: 'blue' } as Record<string, string>)[s] ?? ''; }

  export(): void {
    const t = (k: string) => this.i18n.t(k);
    const head = [t('common.date'), t('emp.number'), t('att.employee'), t('att.location'), t('att.shift'), t('att.in'), t('att.out'),
      t('att.net'), t('att.late'), t('att.lateEx'), t('att.early'), t('att.earlyEx'), t('att.perm'), t('att.ot'), t('allow.title'), t('common.status')];
    const body = this.filtered().map(r => [r.shiftDate, r.employeeNumber, r.employeeName, r.locationName, `${hm(r.scheduledStart)}-${hm(r.scheduledEnd)}`,
      hm(r.checkInAt), hm(r.checkOutAt), hmin(r.netWorkMinutes), hmin(r.lateUnexcused), hmin(r.lateExcused), hmin(r.earlyUnexcused),
      hmin(r.earlyExcused), hmin(r.permissionMinutes), hmin(r.overtimeMinutes), r.allowances.join(' | '), t('state.' + r.status)]);
    downloadCsv(`attendance_${this.from}_${this.to}.csv`, [head, ...body]);
  }
}
