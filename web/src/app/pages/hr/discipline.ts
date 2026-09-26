import { Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { addDays, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Proposal {
  id: string; employeeName: string; employeeNumber: string | null; typeName: string; unit: string; units: number;
  onDate: string; reason: string; status: string; estimatedAmount: number; approvedAmount: number | null;
  decidedByName: string | null; decisionNote: string | null; alternativeWarningLevelId: string | null;
}
interface Level { id: string; nameAr: string; order: number; }
interface Warning { id: string; employeeName: string; levelName: string; reason: string; issuedAt: string; objection: string | null; objectionResponse: string | null; inForce: boolean; }

/**
 * Deductions are proposed by the system and decided by a person: approve, reduce,
 * replace with a warning, or cancel. Nothing here moves money on its own.
 */
@Component({
  selector: 'app-hr-discipline',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop, DecimalPipe],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.discipline' | t }}</h1><span class="spacer"></span>
      @if (auth.canManageHr()) { <button class="btn primary" (click)="scan()" [disabled]="busy()">{{ 'hr.ded.scan' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    <div class="card-pad mb-3"><div class="row">
      <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
      <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
      <div class="field"><label>{{ 'common.status' | t }}</label>
        <select [(ngModel)]="status" (change)="load()">
          @for (s of statuses; track s) { <option [value]="s">{{ 'hr.ded.status.' + s | t }}</option> }</select></div>
      <button class="btn mb-3" (click)="load()">{{ 'common.search' | t }}</button>
    </div>
    <p class="text-xs text-muted">{{ 'hr.ded.lead' | t }}</p></div>

    <div class="tabs">
      <button [class.active]="tab() === 'p'" (click)="tab.set('p')">{{ 'hr.ded.proposals' | t }} ({{ proposals().length }})</button>
      <button [class.active]="tab() === 'w'" (click)="setWarnings()">{{ 'hr.ded.warnings' | t }}</button>
    </div>

    @if (tab() === 'p') {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.date' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'hr.ded.type' | t }}</th>
          <th>{{ 'hr.ded.units' | t }}</th><th>{{ 'hr.ded.amount' | t }}</th><th>{{ 'common.reason' | t }}</th>
          <th>{{ 'common.status' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
        <tbody>
          @for (p of proposals(); track p.id) {
            <tr>
              <td dir="ltr">{{ p.onDate }}</td>
              <td>{{ p.employeeName }}<div class="muted small" dir="ltr">{{ p.employeeNumber }}</div></td>
              <td>{{ p.typeName }}</td>
              <td class="tabular">{{ p.units }} {{ ('hr.ded.unit.' + p.unit) | t }}</td>
              <td class="tabular font-semibold">{{ (p.approvedAmount ?? p.estimatedAmount) | number:'1.2-2' }}</td>
              <td class="whitespace-normal">{{ p.reason }}</td>
              <td><span class="badge" [class.yellow]="p.status === 'Proposed'" [class.red]="p.status === 'Approved'"
                        [class.blue]="p.status === 'ConvertedToWarning'">{{ 'hr.ded.status.' + p.status | t }}</span>
                @if (p.decisionNote) { <div class="muted small">{{ p.decisionNote }}</div> }</td>
              <td>@if (p.status === 'Proposed' && auth.canManageHr()) {
                <button class="btn sm danger" (click)="decide.set(p)">{{ 'hr.ded.decide' | t }}</button> }</td>
            </tr>
          } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.date' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'hr.ded.level' | t }}</th>
          <th>{{ 'common.reason' | t }}</th><th>{{ 'hr.ded.objection' | t }}</th><th>{{ 'common.status' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (w of warnings(); track w.id) {
            <tr><td dir="ltr">{{ w.issuedAt.substring(0,10) }}</td><td>{{ w.employeeName }}</td><td>{{ w.levelName }}</td>
              <td class="whitespace-normal">{{ w.reason }}</td>
              <td class="whitespace-normal">{{ w.objection }}<div class="muted small">{{ w.objectionResponse }}</div></td>
              <td><span class="badge" [class.green]="w.inForce">{{ (w.inForce ? 'hr.ded.inForce' : 'hr.ded.expired') | t }}</span></td>
              <td>@if (w.objection && !w.objectionResponse && auth.canManageHr()) {
                <button class="btn sm" (click)="respond(w)">{{ 'hr.ded.respond' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="7" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }

    @if (decide(); as p) {
      <div class="modal-back" appBackdrop (dismiss)="decide.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ p.employeeName }} — {{ p.typeName }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }

        <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm mb-4">
          <dt class="text-muted">{{ 'common.date' | t }}</dt><dd dir="ltr">{{ p.onDate }}</dd>
          <dt class="text-muted">{{ 'common.reason' | t }}</dt><dd>{{ p.reason }}</dd>
          <dt class="text-muted">{{ 'hr.ded.amount' | t }}</dt><dd class="font-semibold tabular">{{ p.estimatedAmount | number:'1.2-2' }}</dd>
        </dl>

        <div class="field"><label>{{ 'hr.ded.adjust' | t }}</label>
          <input type="number" min="0" step="0.25" [(ngModel)]="units">
          <div class="muted small mt-1">{{ 'hr.ded.adjustHint' | t }}</div></div>
        <div class="field"><label>{{ 'common.reason' | t }}</label><input [(ngModel)]="note"></div>

        <div class="modal-foot">
          <button class="btn" (click)="decide.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn" [disabled]="busy() || !note.trim()" (click)="act(p, 'cancel')">{{ 'hr.ded.cancelIt' | t }}</button>
          <button class="btn" [disabled]="busy() || !note.trim()" (click)="act(p, 'convert')">{{ 'hr.ded.toWarning' | t }}</button>
          <button class="btn primary" [disabled]="busy()" (click)="act(p, 'approve')">{{ 'hr.ded.approve' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class HrDisciplinePage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly proposals = signal<Proposal[]>([]);
  readonly warnings = signal<Warning[]>([]);
  readonly levels = signal<Level[]>([]);
  readonly decide = signal<Proposal | null>(null);
  readonly tab = signal<'p' | 'w'>('p');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly statuses = ['Proposed', 'Approved', 'ConvertedToWarning', 'Cancelled'];
  from = addDays(uaeToday(), -30);
  to = uaeToday();
  status = 'Proposed';
  units = 0;
  note = '';

  async ngOnInit(): Promise<void> {
    this.levels.set(await this.api.get<Level[]>('hr/discipline/warning-levels'));
    await this.load();
  }

  async load(): Promise<void> {
    try { this.proposals.set(await this.api.get<Proposal[]>('hr/discipline/proposals', { status: this.status, from: this.from, to: this.to })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async setWarnings(): Promise<void> {
    this.tab.set('w');
    try { this.warnings.set(await this.api.get<Warning[]>('hr/discipline/warnings')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async scan(): Promise<void> {
    this.busy.set(true);
    try {
      const result = await this.api.post<{ created: number }>('hr/discipline/scan', { from: this.from, to: this.to, employeeId: null });
      this.ui.ok(this.i18n.t('hr.ded.scanned', result.created));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async act(p: Proposal, action: 'approve' | 'convert' | 'cancel'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      if (action === 'approve') {
        await this.api.post(`hr/discipline/proposals/${p.id}/approve`,
          { adjustedUnits: this.units > 0 && this.units !== p.units ? this.units : null, note: this.note || null });
      } else if (action === 'convert') {
        const levelId = p.alternativeWarningLevelId ?? this.levels()[0]?.id;
        if (!levelId) throw new Error('no level');
        await this.api.post(`hr/discipline/proposals/${p.id}/convert-to-warning`, { warningLevelId: levelId, reason: this.note });
      } else {
        await this.api.post(`hr/discipline/proposals/${p.id}/cancel`, { note: this.note });
      }
      this.decide.set(null);
      this.note = '';
      this.units = 0;
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async respond(w: Warning): Promise<void> {
    const response = await this.ui.prompt(this.i18n.t('hr.ded.respond'), this.i18n.t('hr.ded.responseText'));
    if (!response) return;
    try { await this.api.post(`hr/discipline/warnings/${w.id}/respond`, { response }); await this.setWarnings(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
