import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { hm, timeInput, toTimeOnly, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Auth } from '../../core/auth';
import { PendingRequests } from '../../core/pending';
import { Backdrop } from '../../core/backdrop';

interface Perm { id: string; type: string; shiftDate: string; fromTime: string | null; toTime: string | null; status: string; reason: string | null; rejectReason: string | null; createdAt: string; employeeName: string | null; decidedByName: string | null; }
interface Leave { id: string; employeeName: string | null; leaveTypeName: string; fromDate: string; toDate: string; workingDays: number; status: string; reason: string | null; rejectReason: string | null; decidedByName: string | null; }
interface Exc { id: string; employeeName: string; kind: string; requestedAt: string; locationName: string; latitude: number; longitude: number; distanceMeters: number; locationRadius: number; reason: string | null; status: string; rejectReason: string | null; decidedByName: string | null; }

@Component({
  selector: 'app-requests',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 style="margin:0">{{ 'req.title' | t }}</h1><span class="spacer"></span>
      <select style="width:auto" [(ngModel)]="status" (change)="load()">
        @for (s of ['Pending','Approved','Rejected','Cancelled']; track s) { <option [value]="s">{{ 'rs.' + s | t }}</option> }</select>
      @if (workforce() === 'Field' && auth.canManage()) { <button class="btn primary" (click)="openOnBehalf()">+ {{ 'req.onBehalf' | t }}</button> }</div>
    <div class="tabs">
      <button [class.active]="tab() === 'p'" (click)="tab.set('p')">{{ 'req.permissions' | t }} ({{ perms().length }})</button>
      <button [class.active]="tab() === 'l'" (click)="tab.set('l')">{{ 'leave.title' | t }} ({{ leaves().length }})</button>
      <button [class.active]="tab() === 'e'" (click)="tab.set('e')">{{ 'req.exceptions' | t }} ({{ excs().length }})</button>
    </div>

    @if (tab() === 'p') {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'req.type' | t }}</th><th>{{ 'common.date' | t }}</th><th>{{ 'common.from' | t }}</th><th>{{ 'common.to' | t }}</th>
          <th>{{ 'common.reason' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
        <tbody>
          @for (p of perms(); track p.id) {
            <tr><td>{{ p.employeeName }}</td><td>{{ 'perm.' + p.type | t }}</td><td dir="ltr">{{ p.shiftDate }}</td>
              <td dir="ltr">{{ p.fromTime ? timeInput(p.fromTime) : '—' }}</td><td dir="ltr">{{ p.toTime ? timeInput(p.toTime) : '—' }}</td>
              <td style="white-space:normal">{{ p.reason }}</td>
              <td>{{ 'rs.' + p.status | t }} @if (p.decidedByName) { <span class="muted small">· {{ p.decidedByName }}</span> }
                @if (p.rejectReason) { <div class="small muted">{{ p.rejectReason }}</div> }</td>
              <td>@if (p.status === 'Pending') {
                <button class="btn sm primary" (click)="decide('permissions', p.id, true)">{{ 'req.approve' | t }}</button>
                <button class="btn sm danger" (click)="decide('permissions', p.id, false)">{{ 'req.reject' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else if (tab() === 'l') {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'leave.type' | t }}</th><th>{{ 'common.from' | t }}</th><th>{{ 'common.to' | t }}</th>
          <th>{{ 'leave.days' | t }}</th><th>{{ 'common.reason' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
        <tbody>
          @for (l of leaves(); track l.id) {
            <tr><td>{{ l.employeeName }}</td><td>{{ l.leaveTypeName }}</td>
              <td dir="ltr">{{ l.fromDate }}</td><td dir="ltr">{{ l.toDate }}</td>
              <td class="tabular font-semibold">{{ l.workingDays }}</td>
              <td class="whitespace-normal">{{ l.reason }}</td>
              <td>{{ 'rs.' + l.status | t }} @if (l.decidedByName) { <span class="muted small">· {{ l.decidedByName }}</span> }
                @if (l.rejectReason) { <div class="small muted">{{ l.rejectReason }}</div> }</td>
              <td>
                <button class="btn sm ghost" (click)="showChain(l)">{{ 'flow.chain' | t }}</button>
                @if (l.status === 'Pending') {
                  <button class="btn sm primary" (click)="decide('leaves', l.id, true)">{{ 'req.approve' | t }}</button>
                  <button class="btn sm danger" (click)="decide('leaves', l.id, false)">{{ 'req.reject' | t }}</button>
                } @else if (l.status === 'Approved' && auth.canManage()) {
                  <button class="btn sm danger" (click)="cancelLeave(l)">{{ 'leave.cancel' | t }}</button>
                }</td></tr>
          } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'req.type' | t }}</th><th>{{ 'common.date' | t }}</th><th>{{ 'att.location' | t }}</th>
          <th>{{ 'att.distance' | t }}</th><th>{{ 'common.reason' | t }}</th><th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
        <tbody>
          @for (e of excs(); track e.id) {
            <tr><td>{{ e.employeeName }}</td><td>{{ 'ex.' + e.kind | t }}</td><td dir="ltr">{{ e.requestedAt.substring(0,10) }} {{ hm(e.requestedAt) }}</td>
              <td>{{ e.locationName }}</td><td>{{ e.distanceMeters }} m <span class="muted small">/ {{ e.locationRadius }}</span>
                <a class="small" target="_blank" [href]="'https://www.google.com/maps?q=' + e.latitude + ',' + e.longitude">{{ 'common.map' | t }}</a></td>
              <td style="white-space:normal">{{ e.reason }}</td>
              <td>{{ 'rs.' + e.status | t }} @if (e.decidedByName) { <span class="muted small">· {{ e.decidedByName }}</span> }</td>
              <td>@if (e.status === 'Pending') {
                <button class="btn sm primary" (click)="decide('exceptions', e.id, true)">{{ 'req.approve' | t }}</button>
                <button class="btn sm danger" (click)="decide('exceptions', e.id, false)">{{ 'req.reject' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }

    @if (chain(); as steps) {
      <div class="modal-back" appBackdrop (dismiss)="chain.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ 'flow.chain' | t }}</h2>
          <button class="btn sm" (click)="chain.set(null)">{{ 'common.close' | t }}</button></div>
        <ol class="space-y-3">
          @for (s of steps; track s.id) {
            <li class="flex items-start gap-3">
              <span class="mt-1 h-2.5 w-2.5 shrink-0 rounded-full"
                    [class]="s.status === 'Approved' ? 'bg-ok' : s.status === 'Rejected' ? 'bg-bad' : s.isCurrent ? 'bg-warn' : 'bg-line'"></span>
              <div class="flex-1">
                <div class="font-semibold">{{ 'flow.stage.' + s.stage | t }}
                  @if (s.isCurrent) { <span class="badge yellow">{{ 'flow.now' | t }}</span> }</div>
                <div class="text-xs text-muted">{{ s.approverName ?? ('flow.vacant' | t) }} ·
                  {{ 'flow.status.' + s.status | t }}
                  @if (s.decidedAt) { <span dir="ltr">{{ s.decidedAt.substring(0,10) }}</span> }</div>
                @if (s.note) { <div class="text-sm mt-0.5">{{ s.note }}</div> }
              </div>
            </li>
          }
        </ol>
      </div></div>
    }

    @if (onBehalf()) {
      <div class="modal-back" appBackdrop (dismiss)="onBehalf.set(false)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ 'req.onBehalf' | t }}</h2></div>
        <div class="field"><label>{{ 'att.employee' | t }}</label><select [(ngModel)]="ob.employeeId">
          @for (e of employees(); track e.id) { <option [value]="e.id">{{ e.fullName }}</option> }</select></div>
        <div class="row">
          <div class="field"><label>{{ 'req.type' | t }}</label><select [(ngModel)]="ob.type">
            <option value="Late">{{ 'perm.Late' | t }}</option><option value="TemporaryExit">{{ 'perm.TemporaryExit' | t }}</option><option value="EarlyDeparture">{{ 'perm.EarlyDeparture' | t }}</option></select></div>
          <div class="field"><label>{{ 'common.date' | t }}</label><input type="date" [(ngModel)]="ob.shiftDate"></div>
        </div>
        <div class="row">
          @if (ob.type !== 'Late') { <div class="field"><label>{{ 'perm.fromTime' | t }}</label><input type="time" [(ngModel)]="ob.fromTime"></div> }
          @if (ob.type !== 'EarlyDeparture') { <div class="field"><label>{{ (ob.type === 'Late' ? 'perm.until' : 'perm.toTime') | t }}</label><input type="time" [(ngModel)]="ob.toTime"></div> }
        </div>
        <div class="field"><label>{{ 'common.reason' | t }}</label><textarea rows="2" [(ngModel)]="ob.reason"></textarea></div>
        <div class="modal-foot"><button class="btn" (click)="onBehalf.set(false)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="!ob.employeeId || !ob.reason.trim()" (click)="submitOnBehalf()">{{ 'me.send' | t }}</button></div>
      </div></div>
    }`,
})
export class RequestsPage implements OnInit {
  readonly auth = inject(Auth);
  private readonly pending = inject(PendingRequests);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  private readonly i18n = inject(I18n);
  readonly hm = hm;
  readonly timeInput = timeInput;
  readonly tab = signal<'p' | 'l' | 'e'>('p');
  /**
   * Set by the route: Field in the field module, so office requests never show there. Elsewhere
   * (HR, an employee who approves) it is empty: the pending lists are already exactly what waits
   * on the viewer, office or field alike.
   */
  readonly workforce = input<string>('');
  readonly perms = signal<Perm[]>([]);
  readonly excs = signal<Exc[]>([]);
  readonly leaves = signal<Leave[]>([]);
  readonly chain = signal<any[] | null>(null);
  readonly employees = signal<{ id: string; fullName: string }[]>([]);
  readonly onBehalf = signal(false);
  status = 'Pending';
  ob = { employeeId: '', type: 'Late', shiftDate: uaeToday(), fromTime: '', toTime: '', reason: '' };

  ngOnInit(): void { this.load(); this.pending.refresh(); }

  async load(): Promise<void> {
    try {
      const [p, e, l] = await Promise.all([
        this.api.get<Perm[]>('requests/permissions', { status: this.status, workforce: this.workforce() }),
        this.api.get<Exc[]>('requests/exceptions', { status: this.status, workforce: this.workforce() }),
        this.api.get<Leave[]>('requests/leaves', { status: this.status, workforce: this.workforce() }),
      ]);
      this.perms.set(p);
      this.excs.set(e);
      this.leaves.set(l);
    } catch (err) { this.ui.error(this.api.error(err).message); }
  }

  async decide(kind: 'permissions' | 'exceptions' | 'leaves', id: string, approve: boolean): Promise<void> {
    let reason: string | null = null;
    if (!approve) {
      reason = await this.ui.prompt(this.i18n.t('req.reject'), this.i18n.t('req.rejectReason'));
      if (!reason) return;
    }
    try {
      await this.api.post(`requests/${kind}/${id}/${approve ? 'approve' : 'reject'}`, approve ? {} : { reason });
      this.pending.drop();
      this.ui.ok(this.i18n.t('common.saved'));
      await Promise.all([this.load(), this.pending.refresh()]);
    } catch (e) {
      this.ui.error(this.api.error(e).message);
      await Promise.all([this.load(), this.pending.refresh()]);
    }
  }

  /** Shows who has signed and who holds the request now. */
  async showChain(l: Leave): Promise<void> {
    try { this.chain.set(await this.api.get<any[]>(`requests/leaves/${l.id}/timeline`)); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  /** Cancelling an approved leave returns the balance and brings the shifts back. */
  async cancelLeave(l: Leave): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('leave.cancel'), l.employeeName ?? '', true))) return;
    try {
      await this.api.post(`requests/leaves/${l.id}/cancel`);
      this.ui.ok(this.i18n.t('common.saved'));
      await Promise.all([this.load(), this.pending.refresh()]);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async openOnBehalf(): Promise<void> {
    this.employees.set(await this.api.get('employees', { role: 'Collector' }));
    this.ob = { employeeId: this.employees()[0]?.id ?? '', type: 'Late', shiftDate: uaeToday(), fromTime: '', toTime: '', reason: '' };
    this.onBehalf.set(true);
  }

  async submitOnBehalf(): Promise<void> {
    try {
      await this.api.post('requests/permissions', { ...this.ob, fromTime: toTimeOnly(this.ob.fromTime), toTime: toTimeOnly(this.ob.toTime) });
      this.onBehalf.set(false);
      this.ui.ok(this.i18n.t('common.saved'));
      await Promise.all([this.load(), this.pending.refresh()]);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
