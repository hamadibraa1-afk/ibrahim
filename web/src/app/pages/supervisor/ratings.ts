import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { addDays, hm, uaeToday } from '../../core/format';
import { TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Auth } from '../../core/auth';

interface Rating { id: string; scannedAt: string; locationName: string; employeeName: string | null; stars: number; comment: string | null; linkType: string; includedInAverage: boolean; }
interface Page { average: number | null; count: number; byEmployee: { employeeId: string; employeeName: string; average: number; count: number }[]; items: Rating[]; }

@Component({
  selector: 'app-ratings',
  standalone: true,
  imports: [FormsModule, TPipe],
  template: `
    <h1>{{ 'rat.title' | t }}</h1>
    <div class="card" style="margin-bottom:14px"><div class="row">
      <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
      <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
      <button class="btn primary" style="margin-bottom:12px" (click)="load()">{{ 'common.search' | t }}</button></div></div>
    @if (page(); as p) {
      <div class="grid-cards">
        <div class="card"><div class="stat"><div class="num">{{ p.average ?? '—' }}</div><div class="lbl">{{ 'rat.average' | t }}</div></div></div>
        <div class="card"><div class="stat"><div class="num">{{ p.count }}</div><div class="lbl">{{ 'rat.count' | t }}</div></div></div>
      </div>
      <h2>{{ 'rat.byEmployee' | t }}</h2>
      <div class="table-wrap" style="margin-bottom:18px"><table>
        <thead><tr><th>#</th><th>{{ 'att.employee' | t }}</th><th>{{ 'rat.average' | t }}</th><th>{{ 'rat.count' | t }}</th></tr></thead>
        <tbody>@for (e of p.byEmployee; track e.employeeId; let i = $index) {
          <tr><td>{{ i + 1 }}</td><td>{{ e.employeeName }}</td><td>{{ stars(e.average) }} {{ e.average }}</td><td>{{ e.count }}</td></tr>
        } @empty { <tr><td colspan="4" class="muted">{{ 'common.empty' | t }}</td></tr> }</tbody></table></div>
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.date' | t }}</th><th>{{ 'att.location' | t }}</th><th>{{ 'att.employee' | t }}</th><th>★</th>
          <th>{{ 'rat.comment' | t }}</th><th>{{ 'rat.link' | t }}</th><th>{{ 'rat.included' | t }}</th></tr></thead>
        <tbody>@for (r of p.items; track r.id) {
          <tr><td dir="ltr">{{ r.scannedAt.substring(0,10) }} {{ hm(r.scannedAt) }}</td><td>{{ r.locationName }}</td><td>{{ r.employeeName ?? '—' }}</td>
            <td [style.color]="r.stars <= 2 ? 'var(--red)' : ''">{{ stars(r.stars) }}</td><td style="white-space:normal">{{ r.comment }}</td>
            <td><span class="badge" [class.yellow]="r.linkType === 'OutsideShift' || r.linkType === 'NoEmployee'">{{ 'link.' + r.linkType | t }}</span></td>
            <td><input type="checkbox" [checked]="r.includedInAverage" [disabled]="!auth.canManage()" (change)="include(r, $any($event.target).checked)"></td></tr>
        } @empty { <tr><td colspan="7" class="muted">{{ 'common.empty' | t }}</td></tr> }</tbody></table></div>
    }`,
})
export class RatingsPage implements OnInit {
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  from = addDays(uaeToday(), -30);
  to = uaeToday();
  readonly page = signal<Page | null>(null);

  ngOnInit(): void { this.load(); }
  stars(n: number): string { return '★'.repeat(Math.round(n)) + '☆'.repeat(5 - Math.round(n)); }

  async load(): Promise<void> {
    try { this.page.set(await this.api.get<Page>('ratings', { from: this.from, to: this.to })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async include(r: Rating, included: boolean): Promise<void> {
    try { await this.api.post(`ratings/${r.id}/include`, { included }); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
