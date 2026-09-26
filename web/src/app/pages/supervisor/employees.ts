import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Backdrop } from '../../core/backdrop';

interface Person { id: string; fullName: string; employeeNumber: string; email: string | null; phone: string; role: string; preferredLanguage: string; isActive: boolean; rowVersion: string; }
const ROLES = ['SystemAdmin', 'Supervisor', 'DepartmentManager', 'Collector'];

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  styles: [`
    .filters { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .role-tabs { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
    .role-tabs button { padding: 6px 12px; border-radius: 999px; border: 1px solid var(--border); background: var(--surface); cursor: pointer; }
    .role-tabs button.active { background: var(--primary); color: var(--primary-ink); border-color: var(--primary); }
  `],
  template: `
    <div class="toolbar"><h1 style="margin:0">{{ 'emp.title' | t }}</h1><span class="spacer"></span>
      @if (auth.canAdmin()) { <button class="btn primary" (click)="open(null)">+ {{ 'emp.new' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }</div>

    <div class="role-tabs">
      <button [class.active]="role() === ''" (click)="role.set('')">{{ 'common.all' | t }}</button>
      @for (r of roles; track r) { <button [class.active]="role() === r" (click)="role.set(r)">{{ 'role.' + r | t }}</button> }
    </div>

    <div class="card" style="margin-bottom:12px"><div class="filters">
      <input style="max-width:260px" [placeholder]="'common.search' | t" [(ngModel)]="q">
      <label style="display:flex;gap:6px;align-items:center;margin:0"><input type="checkbox" [(ngModel)]="showDeleted" (change)="load()">{{ 'common.showDeleted' | t }}</label>
      <span class="spacer" style="flex:1"></span><span class="muted small">{{ filtered().length }}</span>
    </div></div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'emp.number' | t }}</th><th>{{ 'common.name' | t }}</th><th>{{ 'role.title' | t }}</th><th>{{ 'emp.phone' | t }}</th>
        <th>{{ 'emp.email' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
      <tbody>
        @for (p of filtered(); track p.id) {
          <tr [class.inactive]="!p.isActive">
            <td dir="ltr">{{ p.employeeNumber }}</td>
            <td>{{ p.fullName }} @if (!p.isActive) { <span class="badge">{{ 'common.deleted' | t }}</span> }</td>
            <td><span class="badge" [class.blue]="p.role === 'SystemAdmin'" [class.green]="p.role === 'Supervisor'">{{ 'role.' + p.role | t }}</span></td>
            <td dir="ltr">{{ p.phone }}</td><td dir="ltr">{{ p.email }}</td>
            <td>
              @if (auth.canAdmin()) {
                <button class="btn sm" (click)="open(p)">{{ 'common.edit' | t }}</button>
                <button class="btn sm" (click)="resetPassword(p)">{{ 'emp.resetPassword' | t }}</button>
                @if (p.role === 'Supervisor') { <button class="btn sm" (click)="openSites(p)">{{ 'emp.sites' | t }}</button> }
                @if (p.isActive) { <button class="btn sm danger" (click)="remove(p)">{{ 'common.delete' | t }}</button> }
                @else { <button class="btn sm" (click)="restore(p)">{{ 'common.restore' | t }}</button> }
              } @else { <span class="muted small">—</span> }
            </td>
          </tr>
        } @empty { <tr><td colspan="6" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>

    @if (sites(); as st) {
      <div class="modal-back" appBackdrop (dismiss)="sites.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ 'emp.sites' | t }} — {{ st.person.fullName }}</h2></div>
        <div class="alert blue">{{ 'emp.sitesHint' | t }}</div>
        <div class="max-h-72 overflow-auto rounded-lg border border-line p-2">
          @for (l of locations(); track l.id) {
            <label class="flex items-center gap-2 px-1 py-1 text-ink">
              <input type="checkbox" [checked]="st.selected.includes(l.id)" (change)="toggleSite(st, l.id)">
              <span>{{ l.nameAr }}</span>
            </label>
          }
        </div>
        <div class="modal-foot">
          <button class="btn" (click)="sites.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy()" (click)="saveSites(st)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (editing(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="editing.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ f.id ? f.fullName : ('emp.new' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'common.name' | t }} *</label><input [(ngModel)]="f.fullName"></div>
          <div class="field"><label>{{ 'emp.number' | t }} *</label><input dir="ltr" [(ngModel)]="f.employeeNumber"></div>
        </div>
        <div class="field"><label>{{ 'role.title' | t }} *</label>
          <select [(ngModel)]="f.role">@for (r of roles; track r) { <option [value]="r">{{ 'role.' + r | t }}</option> }</select>
          <div class="muted small" style="margin-top:6px">{{ 'emp.roleHint' | t }}</div></div>
        <div class="row">
          <div class="field"><label>{{ 'emp.phone' | t }} *</label><input dir="ltr" placeholder="05XXXXXXXX" [(ngModel)]="f.phone"></div>
          <div class="field"><label>{{ 'emp.email' | t }} <span class="muted">({{ 'common.optional' | t }})</span></label><input type="email" dir="ltr" [(ngModel)]="f.email"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'common.language' | t }}</label>
            <select [(ngModel)]="f.preferredLanguage"><option value="ar">العربية</option><option value="en">English</option></select></div>
          @if (!f.id) {
            <div class="field"><label>{{ 'emp.password' | t }} *</label><input dir="ltr" [(ngModel)]="f.password">
              <div class="muted small">{{ 'emp.passwordHint' | t }}</div></div>
          }
        </div>
        <div class="modal-foot">
          <button class="btn" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !valid(f)" (click)="save(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class EmployeesPage implements OnInit {
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  private readonly i18n = inject(I18n);
  readonly roles = ROLES;
  readonly items = signal<Person[]>([]);
  readonly role = signal('');
  readonly editing = signal<any>(null);
  readonly sites = signal<any>(null);
  readonly locations = signal<{ id: string; nameAr: string }[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  q = '';
  showDeleted = false;

  readonly filtered = computed(() => {
    const q = this.q.trim().toLowerCase();
    const role = this.role();
    return this.items()
      .filter(p => !role || p.role === role)
      .filter(p => !q || [p.fullName, p.employeeNumber, p.phone, p.email ?? ''].some(v => v.toLowerCase().includes(q)));
  });

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.items.set(await this.api.get<Person[]>('employees', { includeInactive: this.showDeleted })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  /** A supervisor with no sites selected supervises every site. */
  async openSites(p: Person): Promise<void> {
    const [locations, selected] = await Promise.all([
      this.api.get<{ id: string; nameAr: string }[]>('locations'),
      this.api.get<string[]>(`employees/${p.id}/locations`),
    ]);
    this.locations.set(locations);
    this.sites.set({ person: p, selected });
  }

  toggleSite(st: any, id: string): void {
    st.selected = st.selected.includes(id) ? st.selected.filter((x: string) => x !== id) : [...st.selected, id];
  }

  async saveSites(st: any): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.put(`employees/${st.person.id}/locations`, { locationIds: st.selected });
      this.sites.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
    } catch (e) { this.ui.error(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  open(p: Person | null): void {
    this.error.set(null);
    this.editing.set(p
      ? { ...p, password: '' }
      : { id: '', fullName: '', employeeNumber: '', email: '', phone: '', role: 'Collector', preferredLanguage: 'ar', password: '', rowVersion: '' });
  }

  valid(f: any): boolean {
    return !!f.fullName?.trim() && !!f.employeeNumber?.trim() && !!f.phone?.trim() && (!!f.id || f.password?.length >= 8);
  }

  async save(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = { fullName: f.fullName, employeeNumber: f.employeeNumber, email: f.email || null, phone: f.phone, role: f.role, preferredLanguage: f.preferredLanguage };
    try {
      if (f.id) await this.api.put(`employees/${f.id}`, { ...body, rowVersion: f.rowVersion });
      else await this.api.post('employees', { ...body, password: f.password });
      this.editing.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) {
      this.error.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  async remove(p: Person): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + p.fullName, this.i18n.t('common.deleteConfirm'), true))) return;
    await this.call(`employees/${p.id}`, 'delete');
  }

  restore(p: Person): Promise<void> { return this.call(`employees/${p.id}/restore`, 'post'); }

  async resetPassword(p: Person): Promise<void> {
    const pw = await this.ui.prompt(this.i18n.t('emp.resetPassword') + ': ' + p.fullName, this.i18n.t('emp.passwordHint'));
    if (!pw) return;
    try { await this.api.post(`employees/${p.id}/reset-password`, { password: pw }); this.ui.ok(this.i18n.t('common.saved')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  private async call(url: string, method: 'post' | 'delete'): Promise<void> {
    try {
      await (method === 'post' ? this.api.post(url) : this.api.delete(url));
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
