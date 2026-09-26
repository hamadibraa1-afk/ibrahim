import { Component, OnInit, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Row { id: string; employeeName: string; expectedDate: string; actualDate: string | null; status: string; lateDays: number; note: string | null; isOverdue: boolean; }

/** Who is due back from leave, who has reported, and who is late. */
@Component({
  selector: 'app-hr-returns',
  standalone: true,
  imports: [TPipe],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.ret.title' | t }}</h1><span class="spacer"></span>
      <button class="btn sm" (click)="load()">{{ 'common.refresh' | t }}</button></div>
    <p class="mb-4 text-xs text-muted">{{ 'hr.ret.lead' | t }}</p>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'hr.ret.expected' | t }}</th><th>{{ 'hr.ret.actual' | t }}</th>
        <th>{{ 'hr.ret.late' | t }}</th><th>{{ 'sch.notes' | t }}</th><th>{{ 'common.status' | t }}</th><th></th></tr></thead>
      <tbody>
        @for (r of rows(); track r.id) {
          <tr [class.bg-bad-soft]="r.isOverdue">
            <td>{{ r.employeeName }}</td>
            <td dir="ltr" class="tabular">{{ r.expectedDate }}</td>
            <td dir="ltr" class="tabular">{{ r.actualDate ?? '—' }}</td>
            <td class="tabular" [class.text-bad]="r.lateDays > 0">{{ r.lateDays || '—' }}</td>
            <td class="whitespace-normal">{{ r.note }}</td>
            <td><span class="badge" [class.red]="r.isOverdue || r.status === 'Late'"
                      [class.green]="r.status === 'Confirmed'" [class.yellow]="r.status === 'Submitted'">
                  {{ 'hr.ret.status.' + r.status | t }}</span></td>
            <td>@if (r.actualDate && r.status !== 'Confirmed' && auth.canManageHr()) {
              <button class="btn sm primary" (click)="confirm(r)">{{ 'hr.ret.confirm' | t }}</button> }</td></tr>
        } @empty { <tr><td colspan="7" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>`,
})
export class HrReturnsPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly rows = signal<Row[]>([]);

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.rows.set(await this.api.get<Row[]>('hr/approvals/returns', { onlyOpen: true })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async confirm(r: Row): Promise<void> {
    try {
      await this.api.post(`hr/approvals/returns/${r.id}/confirm`);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
