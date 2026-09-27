import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { timeInput, toTimeOnly, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { LeaveDocuments } from '../../layout/leave-documents';

interface Leave { id: string; isPaid: boolean; requiresAttachment: boolean; attachmentCount: number; leaveTypeName: string; fromDate: string; toDate: string; workingDays: number; status: string; reason: string | null; rejectReason: string | null; }
interface Balance { isPaid: boolean; requiresAttachment: boolean; leaveTypeId: string; leaveTypeName: string; totalDays: number | null; usedDays: number; remainingDays: number | null; }
interface Perm { id: string; type: string; shiftDate: string; fromTime: string | null; toTime: string | null; status: string; reason: string | null; rejectReason: string | null; }

@Component({
  selector: 'app-my-requests',
  standalone: true,
  imports: [FormsModule, TPipe, LeaveDocuments],
  template: `
    <h1>{{ 'nav.myRequests' | t }}</h1>
    <div class="card" style="margin-bottom:14px">
      <h2>{{ 'me.newRequest' | t }}</h2>
      <div class="field"><label>{{ 'req.type' | t }}</label><select [(ngModel)]="f.type">
        <option value="Late">{{ 'perm.Late' | t }}</option><option value="TemporaryExit">{{ 'perm.TemporaryExit' | t }}</option>
        <option value="EarlyDeparture">{{ 'perm.EarlyDeparture' | t }}</option><option value="Leave">{{ 'leave.new' | t }}</option></select></div>

      @if (f.type === 'Leave') {
        <div class="field"><label>{{ 'leave.type' | t }}</label>
          <select [(ngModel)]="f.leaveTypeId">@for (b of balances(); track b.leaveTypeId) {
            <option [value]="b.leaveTypeId">{{ b.leaveTypeName }} — {{ (b.isPaid ? 'lt.paid' : 'lt.unpaid') | t }}</option> }</select></div>
        @if (selectedType(); as st) {
          <div class="alert mb-3" [class.blue]="st.isPaid">
            {{ (st.isPaid ? 'leave.paidNote' : 'leave.unpaidNote') | t }}
            @if (st.requiresAttachment) { <div class="mt-1 font-semibold">{{ 'leave.docRequired' | t }}</div> }
          </div>
        }
        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="f.fromDate"></div>
          <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="f.toDate" [min]="f.fromDate"></div>
        </div>
        <p class="mb-3 text-xs text-muted">{{ 'leave.daysHint' | t }}</p>
        <div class="field"><label for="leave-files">{{ 'doc.add' | t }}@if (selectedType()?.requiresAttachment) { * }</label>
          <input id="leave-files" type="file" multiple accept=".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png"
                 (change)="pick($event)" aria-describedby="leave-files-hint">
          <p id="leave-files-hint" class="mt-1 text-xs text-muted">{{ 'doc.hint' | t }}</p>
          @for (file of files; track file.name) { <div class="text-xs">📎 {{ file.name }}</div> }</div>
      } @else {
        <div class="field"><label>{{ 'common.date' | t }}</label><input type="date" [(ngModel)]="f.shiftDate"></div>
        <div class="row">
          @if (f.type !== 'Late') { <div class="field"><label>{{ 'perm.fromTime' | t }}</label><input type="time" [(ngModel)]="f.fromTime"></div> }
          @if (f.type !== 'EarlyDeparture') { <div class="field"><label>{{ (f.type === 'Late' ? 'perm.until' : 'perm.toTime') | t }}</label><input type="time" [(ngModel)]="f.toTime"></div> }
        </div>
      }
      <div class="field"><label>{{ 'common.reason' | t }}</label><textarea rows="2" [(ngModel)]="f.reason"></textarea></div>
      <button class="btn primary big" [disabled]="busy() || !valid()" (click)="submit()">{{ 'me.send' | t }}</button>
    </div>

    @if (balances().length) {
      <div class="card-pad mb-4">
        <h2 class="mb-3">{{ 'leave.balance' | t }}</h2>
        <div class="grid grid-cols-2 gap-2 sm:grid-cols-3">
          @for (b of balances(); track b.leaveTypeId) {
            <div class="rounded-xl bg-raised px-3 py-2.5">
              <div class="text-[11px] text-muted">{{ b.leaveTypeName }}@if (!b.isPaid) { · {{ 'lt.unpaid' | t }} }</div>
              <div class="font-semibold tabular">{{ b.remainingDays ?? ('leave.unlimited' | t) }}</div>
              <div class="text-[11px] text-muted tabular">{{ 'leave.used' | t }}: {{ b.usedDays }}</div>
            </div>
          }
        </div>
      </div>
    }

    @for (l of leaves(); track l.id) {
      <div class="card-pad mb-2.5">
        <div class="flex items-center justify-between gap-2">
          <strong>{{ 'leave.title' | t }} — {{ l.leaveTypeName }}
            @if (!l.isPaid) { <span class="badge yellow">{{ 'lt.unpaid' | t }}</span> }</strong>
          <span class="badge" [class.green]="l.status === 'Approved'" [class.red]="l.status === 'Rejected'" [class.yellow]="l.status === 'Pending'">{{ 'rs.' + l.status | t }}</span>
        </div>
        <div class="text-xs text-muted tabular" dir="ltr">{{ l.fromDate }} → {{ l.toDate }} · {{ l.workingDays }}</div>
        @if (l.reason) { <div class="text-sm mt-1">{{ l.reason }}</div> }
        @if (l.rejectReason) { <div class="text-sm text-bad mt-1">{{ l.rejectReason }}</div> }
        @if (l.requiresAttachment && !l.attachmentCount && l.status === 'Pending') {
          <div class="mt-1 text-sm text-warn">{{ 'leave.docMissing' | t }}</div>
        }
        <div class="mt-2 flex flex-wrap gap-2">
          <button class="btn sm" (click)="docsFor.set(l)">{{ 'doc.title' | t }} ({{ l.attachmentCount }})</button>
          @if (l.status === 'Pending' || l.status === 'Approved') {
            <button class="btn sm" (click)="cancelLeave(l)">{{ 'common.cancel' | t }}</button>
          }
        </div>
      </div>
    }

    @if (docsFor(); as l) {
      <app-leave-documents [leaveId]="l.id" [editable]="l.status === 'Pending'" (closed)="docsFor.set(null); load()" />
    }

    @for (p of items(); track p.id) {
      <div class="card" style="margin-bottom:10px">
        <div style="display:flex;justify-content:space-between;gap:8px;align-items:center">
          <strong>{{ 'perm.' + p.type | t }}</strong>
          <span class="badge" [class.green]="p.status === 'Approved'" [class.red]="p.status === 'Rejected'" [class.yellow]="p.status === 'Pending'">{{ 'rs.' + p.status | t }}</span>
        </div>
        <div class="muted small" dir="ltr">{{ p.shiftDate }} {{ timeInput(p.fromTime) }} {{ p.toTime ? '→ ' + timeInput(p.toTime) : '' }}</div>
        @if (p.reason) { <div class="small">{{ p.reason }}</div> }
        @if (p.rejectReason) { <div class="small" style="color:var(--red)">{{ p.rejectReason }}</div> }
        @if (p.status === 'Pending' || p.status === 'Approved') {
          <button class="btn sm" style="margin-top:6px" (click)="cancel(p)">{{ 'common.cancel' | t }}</button>
        }
      </div>
    }`,
})
export class MyRequestsPage implements OnInit {
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  private readonly i18n = inject(I18n);
  readonly type = input<string>();
  readonly timeInput = timeInput;
  readonly items = signal<Perm[]>([]);
  readonly leaves = signal<Leave[]>([]);
  readonly balances = signal<Balance[]>([]);
  readonly busy = signal(false);
  readonly docsFor = signal<Leave | null>(null);
  files: File[] = [];
  f = { type: 'Late', shiftDate: uaeToday(), fromTime: '', toTime: '', reason: '', leaveTypeId: '', fromDate: uaeToday(), toDate: uaeToday() };

  ngOnInit(): void {
    const t = this.type();
    if (t) this.f.type = t;
    this.load();
  }

  valid(): boolean {
    const f = this.f;
    if (f.type === 'Leave') return !!f.leaveTypeId && !!f.fromDate && !!f.toDate && f.toDate >= f.fromDate
      && (!this.selectedType()?.requiresAttachment || this.files.length > 0);
    return !!f.reason.trim() && !!f.shiftDate && (f.type === 'Late' || !!f.fromTime) && (f.type === 'EarlyDeparture' || !!f.toTime);
  }

  selectedType(): Balance | undefined { return this.balances().find(b => b.leaveTypeId === this.f.leaveTypeId); }

  pick(event: Event): void { this.files = Array.from((event.target as HTMLInputElement).files ?? []); }

  async load(): Promise<void> {
    const [perms, leaves, balances] = await Promise.all([
      this.api.get<Perm[]>('me/permissions'),
      this.api.get<Leave[]>('me/leaves'),
      this.api.get<Balance[]>('me/leaves/balances'),
    ]);
    this.items.set(perms);
    this.leaves.set(leaves);
    this.balances.set(balances);
    if (!this.f.leaveTypeId) this.f.leaveTypeId = balances[0]?.leaveTypeId ?? '';
  }

  async cancelLeave(l: Leave): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('leave.cancel'), l.leaveTypeName))) return;
    try { await this.api.post(`me/leaves/${l.id}/cancel`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    try {
      if (this.f.type === 'Leave') {
        const leave = await this.api.post<{ id: string }>('me/leaves', { leaveTypeId: this.f.leaveTypeId, fromDate: this.f.fromDate, toDate: this.f.toDate, reason: this.f.reason || null });
        // The request exists now; a file that fails to upload can still be added from its card.
        try { for (const file of this.files) await this.api.upload(`me/leaves/${leave.id}/attachments`, file); }
        catch (e) { this.ui.error(this.api.error(e).message); }
        this.files = [];
      } else {
        await this.api.post('me/permissions', { type: this.f.type, shiftDate: this.f.shiftDate, reason: this.f.reason, fromTime: this.f.type === 'Late' ? null : toTimeOnly(this.f.fromTime), toTime: this.f.type === 'EarlyDeparture' ? null : toTimeOnly(this.f.toTime) });
      }
      this.f = { ...this.f, fromTime: '', toTime: '', reason: '' };
      this.ui.ok('✓');
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
    finally { this.busy.set(false); }
  }

  async cancel(p: Perm): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.cancel') + ' — ' + this.i18n.t('perm.' + p.type)))) return;
    try { await this.api.post(`me/permissions/${p.id}/cancel`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
