import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { addDays, hm, uaeToday, weekStart, weekday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Backdrop } from '../../core/backdrop';

interface GridShift { locationId: string; locationNameAr: string; locationNameEn: string; shiftTemplateId: string; shiftNameAr: string; shiftNameEn: string; start: string; end: string; source: 'BaseAssignment' | 'Override'; sourceId: string; overrideType: string | null; isOnLeave: boolean; }
interface GridCell { date: string; shifts: GridShift[]; }
interface GridRow { employeeId: string; employeeName: string; cells: GridCell[]; }
interface Warning { kind: 'Uncovered' | 'Capacity'; date: string; locationId: string; locationNameAr: string; locationNameEn: string; detail: string | null; }
interface Grid { dates: string[]; rows: GridRow[]; warnings: Warning[]; }
interface Named { id: string; nameAr: string; nameEn: string; isActive?: boolean; startTime?: string; endTime?: string; }

type Action = 'assign' | 'cover' | 'extend' | 'temp' | 'perm' | 'replace' | 'cancel' | 'end' | 'undo';

@Component({
  selector: 'app-schedule',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  styles: [`
    .bar { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; margin-bottom: 12px; }
    .range { font-weight: 600; }
    .notes { border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); margin-bottom: 14px; overflow: hidden; }
    .notes-head { display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: var(--surface-2); cursor: pointer; }
    .notes-head .count { background: var(--yellow-soft); color: var(--yellow); border-radius: 999px; padding: 1px 10px; font-weight: 700; font-size: .8rem; }
    .notes-body { padding: 8px 14px 12px; display: grid; gap: 6px; max-height: 210px; overflow: auto; }
    .note { display: flex; gap: 10px; align-items: center; padding: 7px 10px; border-radius: 8px; background: var(--surface-2); cursor: pointer; font-size: .87rem; }
    .note:hover { outline: 1px solid var(--primary); }
    .note .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--red); flex: none; }
    .note.capacity .dot { background: var(--yellow); }
    .note .when { color: var(--muted); font-size: .8rem; }
    .legend { display: flex; gap: 14px; flex-wrap: wrap; font-size: .8rem; color: var(--muted); align-items: center; margin-bottom: 10px; }
    .legend .chip { margin: 0; }
    table.grid { font-size: .85rem; }
    .grid th, .grid td { min-width: 132px; vertical-align: top; padding: 6px; }
    .grid td:first-child, .grid th:first-child { position: sticky; inset-inline-start: 0; background: var(--surface); z-index: 2; min-width: 168px; font-weight: 600; }
    .grid th { text-align: center; }
    .grid th.today { background: var(--primary-soft); color: var(--primary); }
    .grid th .dow { font-weight: 700; } .grid th .dnum { font-size: .75rem; color: var(--muted); }
    .cell { min-height: 46px; border-radius: 8px; padding: 4px; cursor: pointer; border: 1px dashed transparent; transition: background .12s; }
    .cell:hover { background: var(--surface-2); border-color: var(--border); }
    .cell.flag { box-shadow: inset 0 0 0 1px var(--yellow); }
    .chip { display: block; padding: 5px 7px; border-radius: 7px; margin-bottom: 4px; background: var(--primary-soft); color: var(--primary);
      border-inline-start: 3px solid var(--primary); line-height: 1.35; white-space: normal; }
    .chip.override { background: var(--blue-soft); color: var(--blue); border-inline-start-color: var(--blue); }
    .chip.leave { background: var(--grey-soft); color: var(--grey); border-inline-start-color: var(--grey); }
    .chip .time { font-size: .75rem; opacity: .85; }
    .empty { color: var(--border); text-align: center; font-size: 1.1rem; line-height: 38px; }
    .actions { display: grid; grid-template-columns: repeat(auto-fill, minmax(165px, 1fr)); gap: 6px; margin: 10px 0 14px; }
    .actions button.active { background: var(--primary); color: var(--primary-ink); border-color: var(--primary); }
    .info { display: grid; grid-template-columns: auto 1fr; gap: 6px 14px; margin-bottom: 12px; font-size: .9rem; }
    .info .k { color: var(--muted); }
    .days { display: flex; gap: 8px; flex-wrap: wrap; } .days label { display: flex; gap: 4px; align-items: center; color: var(--text); margin: 0; }
  `],
  template: `
    <div class="bar">
      <h1 style="margin:0">{{ 'sch.title' | t }}</h1><span class="spacer" style="flex:1"></span>
      <span class="range" dir="ltr">{{ from }} → {{ lastDate() }}</span>
      <button class="btn sm" (click)="move(-1)">‹ {{ 'sch.prev' | t }}</button>
      <button class="btn sm" (click)="goToday()">{{ 'sch.today' | t }}</button>
      <button class="btn sm" (click)="move(1)">{{ 'sch.next' | t }} ›</button>
      <select style="width:auto" [(ngModel)]="span" (change)="load()">
        <option [ngValue]="7">{{ 'sch.week' | t }}</option><option [ngValue]="14">{{ 'sch.twoWeeks' | t }}</option><option [ngValue]="31">{{ 'sch.month' | t }}</option>
      </select>
      @if (!auth.canManage()) { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    @if (grid(); as g) {
      @if (g.warnings.length) {
        <div class="notes">
          <div class="notes-head" (click)="showNotes.set(!showNotes())">
            <strong>{{ 'sch.warnings' | t }}</strong><span class="count">{{ g.warnings.length }}</span>
            <span class="spacer" style="flex:1"></span>
            <span class="muted small">{{ (showNotes() ? 'sch.hideWarnings' : 'sch.showWarnings') | t }}</span>
          </div>
          @if (showNotes()) {
            <div class="notes-body">
              @for (w of g.warnings; track w.kind + w.date + w.locationId) {
                <div class="note" [class.capacity]="w.kind === 'Capacity'" (click)="openWarning(w)">
                  <span class="dot"></span>
                  <span>{{ (w.kind === 'Uncovered' ? 'sch.uncovered' : 'sch.capacity') | t }}</span>
                  <strong>{{ i18n.pick(w.locationNameAr, w.locationNameEn) }}</strong>
                  @if (w.detail) { <span class="badge yellow">{{ w.detail }}</span> }
                  <span class="spacer" style="flex:1"></span>
                  <span class="when" dir="ltr">{{ w.date }} · {{ ('day.' + weekdayOf(w.date)) | t }}</span>
                </div>
              }
            </div>
          }
        </div>
      }

      <div class="legend">
        <span>{{ 'sch.legend' | t }}:</span>
        <span class="chip">{{ 'sch.base' | t }}</span>
        <span class="chip override">{{ 'sch.override' | t }}</span>
        <span class="chip leave">{{ 'sch.leave' | t }}</span>
      </div>

      <div class="table-wrap" style="max-height:66vh"><table class="grid">
        <thead><tr><th>{{ 'att.employee' | t }}</th>
          @for (d of g.dates; track d) {
            <th [class.today]="d === today"><div class="dow">{{ ('day.' + weekdayOf(d)) | t }}</div><div class="dnum" dir="ltr">{{ d.substring(5) }}</div></th>
          }</tr></thead>
        <tbody>
          @for (r of g.rows; track r.employeeId) {
            <tr><td>{{ r.employeeName }}</td>
              @for (c of r.cells; track c.date) {
                <td><div class="cell" [class.flag]="cellFlagged(c)" (click)="openCell(r, c)">
                  @for (s of c.shifts; track s.sourceId + s.start) {
                    <span class="chip" [class.override]="s.source === 'Override'" [class.leave]="s.isOnLeave">
                      {{ i18n.pick(s.locationNameAr, s.locationNameEn) }}
                      <span class="time" dir="ltr">{{ hm(s.start) }}–{{ hm(s.end) }}</span>
                      @if (s.overrideType) { <span class="time">· {{ ('ov.' + s.overrideType) | t }}</span> }
                    </span>
                  } @empty { <div class="empty">+</div> }
                </div></td>
              }
            </tr>
          } @empty { <tr><td [attr.colspan]="g.dates.length + 1" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else { <p class="muted">{{ 'common.loading' | t }}</p> }

    @if (sel(); as s) {
      <div class="modal-back" appBackdrop (dismiss)="sel.set(null)"><div class="modal wide" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ s.row.employeeName }} · <span dir="ltr">{{ s.cell.date }}</span> {{ ('day.' + weekdayOf(s.cell.date)) | t }}</h2>
          <button class="btn sm" (click)="sel.set(null)">{{ 'common.close' | t }}</button></div>

        <h2>{{ 'sch.dayInfo' | t }}</h2>
        @if (s.shift; as sh) {
          <div class="info">
            <span class="k">{{ 'sch.location' | t }}</span><span>{{ i18n.pick(sh.locationNameAr, sh.locationNameEn) }}</span>
            <span class="k">{{ 'sch.shift' | t }}</span><span>{{ i18n.pick(sh.shiftNameAr, sh.shiftNameEn) }} · <span dir="ltr">{{ hm(sh.start) }}–{{ hm(sh.end) }}</span></span>
            <span class="k">{{ 'sch.source' | t }}</span><span>{{ (sh.source === 'Override' ? 'sch.override' : 'sch.base') | t }}
              @if (sh.overrideType) { · {{ ('ov.' + sh.overrideType) | t }} }</span>
            @if (sh.isOnLeave) { <span class="k">{{ 'common.status' | t }}</span><span class="badge blue">{{ 'sch.leave' | t }}</span> }
          </div>
        } @else { <p class="muted">{{ 'sch.cellEmpty' | t }}</p> }

        <h2>{{ 'sch.dayWarnings' | t }}</h2>
        @if (cellWarnings(s.cell, s.shift); as list) {
          @for (w of list; track w.kind + w.locationId) {
            <div class="alert" [class.red]="w.kind === 'Uncovered'">
              {{ (w.kind === 'Uncovered' ? 'sch.uncovered' : 'sch.capacity') | t }} — {{ i18n.pick(w.locationNameAr, w.locationNameEn) }}
              @if (w.detail) { ({{ w.detail }}) }
            </div>
          } @empty { <p class="muted small">{{ 'sch.noWarnings' | t }}</p> }
        }

        @if (auth.canManage()) {
          <h2>{{ 'sch.chooseAction' | t }}</h2>
          <div class="actions">
            @for (a of actionsFor(s.shift); track a.key) {
              <button class="btn" [class.active]="action() === a.key" (click)="pick(a.key)">{{ a.label | t }}</button>
            }
          </div>

          @if (action(); as a) {
            @if (needsLocation(a)) {
              <div class="row">
                <div class="field"><label>{{ 'sch.location' | t }}</label>
                  <select [(ngModel)]="f.locationId">@for (l of activeLocations(); track l.id) { <option [value]="l.id">{{ i18n.pick(l.nameAr, l.nameEn) }}</option> }</select></div>
                <div class="field"><label>{{ 'sch.shift' | t }}</label>
                  <select [(ngModel)]="f.shiftId">@for (x of activeShifts(); track x.id) { <option [value]="x.id">{{ i18n.pick(x.nameAr, x.nameEn) }} (<span dir="ltr">{{ x.startTime?.substring(0,5) }}–{{ x.endTime?.substring(0,5) }}</span>)</option> }</select></div>
              </div>
            }
            @if (a === 'assign') {
              <div class="row">
                <div class="field"><label>{{ 'sch.startDate' | t }}</label><input type="date" [(ngModel)]="f.from" [min]="today"></div>
                <div class="field"><label>{{ 'sch.endDate' | t }}</label><input type="date" [(ngModel)]="f.to" [min]="f.from"></div>
              </div>
              <div class="field"><label>{{ 'sch.days' | t }}</label><div class="days">
                @for (d of [0,1,2,3,4,5,6]; track d) { <label><input type="checkbox" [checked]="hasDay(d)" (change)="toggleDay(d)">{{ ('day.' + d) | t }}</label> }</div></div>
              <div class="field"><label>{{ 'sch.notes' | t }}</label><input [(ngModel)]="f.reason"></div>
            }
            @if (a === 'cover' || a === 'temp' || a === 'replace' || a === 'cancel') {
              <div class="row">
                <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="f.from" [min]="today"></div>
                <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="f.to" [min]="f.from"></div>
              </div>
            }
            @if (a === 'replace') {
              <div class="field"><label>{{ 'sch.replacement' | t }}</label>
                @if (!available().length) { <div class="alert">{{ 'sch.noAvailable' | t }}</div> }
                <select [(ngModel)]="f.replacementId">@for (e of available(); track e.id) { <option [value]="e.id">{{ e.fullName }}</option> }</select></div>
            }
            @if (a === 'extend') { <div class="field"><label>{{ 'sch.endDate' | t }}</label><input type="date" [(ngModel)]="f.to" [min]="today"></div> }
            @if (a === 'perm' || a === 'end') { <div class="field"><label>{{ 'sch.effective' | t }}</label><input type="date" [(ngModel)]="f.from" [min]="today"></div> }
            @if (a === 'temp' || a === 'replace' || a === 'cancel' || a === 'cover') {
              <div class="field"><label>{{ 'common.reason' | t }} <span class="muted">({{ 'common.optional' | t }})</span></label><input [(ngModel)]="f.reason"></div>
            }
            <div class="modal-foot">
              <button class="btn" (click)="action.set(null)">{{ 'common.cancel' | t }}</button>
              <button class="btn primary" [disabled]="busy()" (click)="apply(a)">{{ 'sch.execute' | t }}</button>
            </div>
          }
        }
      </div></div>
    }`,
})
export class SchedulePage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly today = uaeToday();
  from = weekStart(this.today);
  span = 7;

  readonly grid = signal<Grid | null>(null);
  readonly locations = signal<Named[]>([]);
  readonly shifts = signal<Named[]>([]);
  readonly available = signal<{ id: string; fullName: string }[]>([]);
  readonly sel = signal<{ row: GridRow; cell: GridCell; shift: GridShift | null } | null>(null);
  readonly action = signal<Action | null>(null);
  readonly busy = signal(false);
  readonly showNotes = signal(true);
  readonly activeLocations = computed(() => this.locations().filter(l => l.isActive !== false));
  readonly activeShifts = computed(() => this.shifts().filter(s => s.isActive !== false));

  f = { locationId: '', shiftId: '', from: '', to: '', days: 31, reason: '', replacementId: '' };

  async ngOnInit(): Promise<void> {
    const [l, s] = await Promise.all([this.api.get<Named[]>('locations'), this.api.get<Named[]>('shifts')]);
    this.locations.set(l);
    this.shifts.set(s);
    await this.load();
  }

  async load(): Promise<void> {
    try { this.grid.set(await this.api.get<Grid>('schedule/grid', { from: this.from, to: addDays(this.from, this.span - 1) })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  lastDate(): string { return addDays(this.from, this.span - 1); }
  move(dir: number): void { this.from = addDays(this.from, dir * this.span); this.load(); }
  goToday(): void { this.from = weekStart(this.today); this.load(); }
  weekdayOf(d: string): number { return weekday(d); }

  /** Warnings that belong to one cell: same day, and same location as the shift (or any, when the cell is empty). */
  cellWarnings(cell: GridCell, shift: GridShift | null): Warning[] {
    const all = this.grid()?.warnings ?? [];
    return all.filter(w => w.date === cell.date && (!shift || w.locationId === shift.locationId));
  }

  cellFlagged(cell: GridCell): boolean {
    const all = this.grid()?.warnings ?? [];
    return cell.shifts.some(s => all.some(w => w.date === cell.date && w.locationId === s.locationId));
  }

  /** Jumps from a note to the day it belongs to, ready to assign cover. */
  openWarning(w: Warning): void {
    const g = this.grid();
    if (!g) return;
    const row = g.rows.find(r => r.cells.some(c => c.date === w.date && c.shifts.length === 0)) ?? g.rows[0];
    if (!row) return;
    const cell = row.cells.find(c => c.date === w.date)!;
    this.openCell(row, cell);
    this.f.locationId = w.locationId;
    if (w.kind === 'Uncovered' && this.auth.canManage()) this.pick(cell.shifts.length ? 'temp' : 'cover');
  }

  actionsFor(shift: GridShift | null): { key: Action; label: string }[] {
    if (!shift) return [{ key: 'assign', label: 'sch.assign' }, { key: 'cover', label: 'sch.cover' }];
    const common: { key: Action; label: string }[] = [
      { key: 'temp', label: 'sch.tempTransfer' }, { key: 'replace', label: 'sch.replace' }, { key: 'cancel', label: 'sch.cancelDays' },
    ];
    return shift.source === 'BaseAssignment'
      ? [{ key: 'extend', label: 'sch.extend' }, ...common, { key: 'perm', label: 'sch.permTransfer' }, { key: 'end', label: 'sch.endAssignment' }]
      : [{ key: 'undo', label: 'sch.undoOverride' }, ...common];
  }

  needsLocation(a: Action): boolean { return a === 'assign' || a === 'cover' || a === 'temp' || a === 'perm'; }

  openCell(row: GridRow, cell: GridCell): void {
    const shift = cell.shifts[0] ?? null;
    const date = cell.date < this.today ? this.today : cell.date;
    this.f = {
      locationId: shift?.locationId ?? this.activeLocations()[0]?.id ?? '', shiftId: shift?.shiftTemplateId ?? this.activeShifts()[0]?.id ?? '',
      from: date, to: date, days: 31, reason: '', replacementId: '',
    };
    this.action.set(null);
    this.sel.set({ row, cell, shift });
  }

  async pick(a: Action): Promise<void> {
    this.action.set(a);
    if (a === 'extend' || a === 'assign') this.f.to = '';
    if (a === 'replace') {
      const list = await this.api.get<{ id: string; fullName: string }[]>('schedule/available', { date: this.f.from });
      this.available.set(list);
      this.f.replacementId = list[0]?.id ?? '';
    }
  }

  hasDay(d: number): boolean { return (this.f.days & (1 << d)) !== 0; }
  toggleDay(d: number): void { this.f.days ^= 1 << d; }

  async apply(a: Action): Promise<void> {
    const s = this.sel();
    if (!s) return;
    const employeeId = s.row.employeeId;
    const f = this.f;
    const override = (type: string, extra: object = {}) => this.api.post('schedule/overrides', {
      type, employeeId, fromDate: f.from, toDate: f.to || f.from, locationId: f.locationId, shiftTemplateId: f.shiftId, reason: f.reason || null, ...extra,
    });

    this.busy.set(true);
    try {
      switch (a) {
        case 'assign':
          await this.api.post('schedule/assignments', { employeeId, locationId: f.locationId, shiftTemplateId: f.shiftId, startDate: f.from, endDate: f.to || null, days: f.days, notes: f.reason || null });
          break;
        case 'cover': await override('EmergencyCover'); break;
        case 'temp': await override('TemporaryTransfer'); break;
        case 'replace':
          await override('Replacement', { locationId: s.shift!.locationId, shiftTemplateId: s.shift!.shiftTemplateId, replacementEmployeeId: f.replacementId });
          break;
        case 'cancel':
          await this.api.post('schedule/overrides', { type: 'Cancel', employeeId, fromDate: f.from, toDate: f.to || f.from, reason: f.reason || null });
          break;
        case 'extend': await this.api.put(`schedule/assignments/${s.shift!.sourceId}/end-date`, { endDate: f.to || null }); break;
        case 'perm': await this.api.post(`schedule/assignments/${s.shift!.sourceId}/transfer`, { locationId: f.locationId, shiftTemplateId: f.shiftId, effectiveDate: f.from }); break;
        case 'end':
          if (!(await this.ui.confirm(this.i18n.t('sch.endAssignment'), s.row.employeeName, true))) return;
          await this.api.post(`schedule/assignments/${s.shift!.sourceId}/end`, { effectiveDate: f.from });
          break;
        case 'undo': await this.api.post(`schedule/overrides/${s.shift!.sourceId}/deactivate`); break;
      }
      this.sel.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) {
      this.ui.error(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
