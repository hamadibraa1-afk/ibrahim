import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { hm, hmin } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Item {
  id: string; reference: string; kind: string; status: string; submittedAt: string; locationName: string;
  employeeName: string | null; customerName: string; customerPhone: string; customerEmail: string | null;
  message: string; assignedToName: string | null; resolutionNote: string | null; closedAt: string | null; handlingMinutes: number | null;
}

@Component({
  selector: 'app-feedback-admin',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'nav.feedback' | t }}</h1><span class="spacer"></span>
      <select style="width:auto" [(ngModel)]="status" (change)="load()">
        <option value="">{{ 'common.all' | t }}</option>
        @for (s of statuses; track s) { <option [value]="s">{{ 'fbs.' + s | t }}</option> }
      </select>
      <button class="btn sm" (click)="load()">{{ 'common.refresh' | t }}</button>
    </div>

    <div class="tabs">
      <button [class.active]="kind() === 'Complaint'" (click)="setKind('Complaint')">{{ 'fb.complaints' | t }}</button>
      <button [class.active]="kind() === 'Suggestion'" (click)="setKind('Suggestion')">{{ 'fb.suggestions' | t }}</button>
    </div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'fb.reference' | t }}</th><th>{{ 'common.date' | t }}</th><th>{{ 'att.location' | t }}</th>
        <th>{{ 'fb.customer' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'common.status' | t }}</th>
        <th>{{ 'fb.assignee' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
      <tbody>
        @for (f of items(); track f.id) {
          <tr>
            <td class="font-semibold tabular" dir="ltr">{{ f.reference }}</td>
            <td dir="ltr" class="tabular">{{ f.submittedAt.substring(0, 10) }} {{ hm(f.submittedAt) }}</td>
            <td>{{ f.locationName }}</td>
            <td>{{ f.customerName }}<div class="muted small" dir="ltr">{{ f.customerPhone }}</div></td>
            <td>{{ f.employeeName ?? '—' }}</td>
            <td><span class="badge" [class.red]="f.status === 'New'" [class.yellow]="f.status === 'InProgress' || f.status === 'Escalated'"
                      [class.green]="f.status === 'Closed'">{{ 'fbs.' + f.status | t }}</span></td>
            <td>{{ f.assignedToName ?? '—' }}</td>
            <td><button class="btn sm" (click)="open.set(f)">{{ 'common.details' | t }}</button></td>
          </tr>
        } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>

    @if (open(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="open.set(null)"><div class="modal wide">
        <div class="modal-head">
          <h2>{{ ('fb.' + (f.kind === 'Suggestion' ? 'suggestion' : 'complaint')) | t }} · <span dir="ltr">{{ f.reference }}</span></h2>
          <button class="btn sm" (click)="open.set(null)">{{ 'common.close' | t }}</button>
        </div>

        <div class="rounded-xl bg-raised p-4 whitespace-pre-line">{{ f.message }}</div>

        <dl class="mt-4 grid grid-cols-[auto_1fr] gap-x-4 gap-y-2.5 text-sm">
          <dt class="text-muted">{{ 'fb.customer' | t }}</dt><dd>{{ f.customerName }}</dd>
          <dt class="text-muted">{{ 'fb.phone' | t }}</dt><dd dir="ltr"><a [href]="'tel:' + f.customerPhone">{{ f.customerPhone }}</a></dd>
          @if (f.customerEmail) { <dt class="text-muted">{{ 'fb.email' | t }}</dt><dd dir="ltr"><a [href]="'mailto:' + f.customerEmail">{{ f.customerEmail }}</a></dd> }
          <dt class="text-muted">{{ 'att.location' | t }}</dt><dd>{{ f.locationName }}</dd>
          <dt class="text-muted">{{ 'att.employee' | t }}</dt><dd>{{ f.employeeName ?? '—' }}</dd>
          @if (f.handlingMinutes !== null) { <dt class="text-muted">{{ 'fb.handling' | t }}</dt><dd>{{ hmin(f.handlingMinutes) }}</dd> }
          @if (f.resolutionNote) { <dt class="text-muted">{{ 'fb.note' | t }}</dt><dd class="whitespace-pre-line">{{ f.resolutionNote }}</dd> }
        </dl>

        @if (auth.canManage()) {
          <div class="modal-foot">
            @if (f.status !== 'Closed') {
              <button class="btn" (click)="act(f, 'take')">{{ 'fb.take' | t }}</button>
              <button class="btn" (click)="escalate(f)">{{ 'fb.escalate' | t }}</button>
              <button class="btn primary" (click)="close(f)">{{ 'fb.closeItem' | t }}</button>
            } @else {
              <button class="btn" (click)="act(f, 'reopen')">{{ 'fb.reopen' | t }}</button>
            }
          </div>
        }
      </div></div>
    }`,
})
export class FeedbackAdminPage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly hmin = hmin;
  readonly items = signal<Item[]>([]);
  readonly kind = signal('Complaint');
  readonly open = signal<Item | null>(null);
  readonly statuses = ['New', 'InProgress', 'Escalated', 'Closed'];
  status = '';

  ngOnInit(): void { this.load(); }

  setKind(k: string): void { this.kind.set(k); this.load(); }

  async load(): Promise<void> {
    try { this.items.set(await this.api.get<Item[]>('feedback', { kind: this.kind(), status: this.status })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async act(f: Item, action: string, body: object = {}): Promise<void> {
    try {
      await this.api.post(`feedback/${f.id}/${action}`, body);
      this.open.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async escalate(f: Item): Promise<void> {
    const note = await this.ui.prompt(this.i18n.t('fb.escalate'), this.i18n.t('fb.note'), false);
    if (note === null) return;
    await this.act(f, 'escalate', { note });
  }

  async close(f: Item): Promise<void> {
    const note = await this.ui.prompt(this.i18n.t('fb.closeItem'), this.i18n.t('fb.resolution'));
    if (!note) return;
    await this.act(f, 'close', { note });
  }
}
