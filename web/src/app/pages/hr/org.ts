import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { getPosition } from '../../core/format';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { MapPicker } from '../../layout/map-picker';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Department { id: string; nameAr: string; nameEn: string; branchLocationId: string | null; branchName: string | null; managerId: string | null; managerName: string | null; sectionCount: number; employeeCount: number; rowVersion: string; }
interface Section { id: string; departmentId: string; departmentName: string; nameAr: string; nameEn: string; headId: string | null; headName: string | null; employeeCount: number; rowVersion: string; }

@Component({
  selector: 'app-hr-org',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop, MapPicker, DecimalPipe],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.org' | t }}</h1><span class="spacer"></span>
      @if (auth.canManageHr()) {
        <button class="btn" (click)="openBranch(null)">+ {{ 'hr.org.newBranch' | t }}</button>
        <button class="btn" (click)="openSection(null)">+ {{ 'hr.org.newSection' | t }}</button>
        <button class="btn primary" (click)="openDepartment(null)">+ {{ 'hr.org.newDepartment' | t }}</button>
      } @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    <section class="card mb-5 overflow-hidden">
      <header class="flex items-center gap-2 border-b border-line px-4 py-3">
        <h2 class="flex-1">{{ 'hr.org.branches' | t }}</h2>
        <span class="badge">{{ branches().length }}</span>
      </header>
      <div class="divide-y divide-line">
        @for (b of branches(); track b.id) {
          <div class="flex flex-wrap items-center gap-3 px-4 py-3">
            <span class="min-w-[10rem] flex-1 font-semibold">{{ b.nameAr }}
              <span class="muted small">{{ b.address }}</span></span>
            <span class="text-xs text-muted tabular" dir="ltr">{{ b.latitude | number:'1.4-4' }}, {{ b.longitude | number:'1.4-4' }}</span>
            <span class="badge">{{ 'loc.radius' | t }}: {{ b.radiusMeters }}</span>
            @if (auth.canManageHr()) {
              <button class="btn sm" (click)="openBranch(b)">{{ 'common.edit' | t }}</button>
            }
          </div>
        } @empty { <p class="px-4 py-6 text-center text-sm text-muted">{{ 'hr.org.noBranches' | t }}</p> }
      </div>
      <p class="border-t border-line px-4 py-3 text-xs text-muted">{{ 'hr.org.branchHint' | t }}</p>
    </section>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (d of departments(); track d.id) {
        <section class="card overflow-hidden">
          <header class="flex items-center gap-2 border-b border-line px-4 py-3">
            <h2 class="flex-1">{{ d.nameAr }}</h2>
            <span class="badge">{{ d.employeeCount }}</span>
            @if (auth.canManageHr()) {
              <button class="btn sm" (click)="openDepartment(d)">{{ 'common.edit' | t }}</button>
              <button class="btn sm danger" (click)="removeDepartment(d)">{{ 'common.delete' | t }}</button>
            }
          </header>
          <div class="px-4 py-3 text-sm">
            <div class="flex flex-wrap gap-x-6 gap-y-1 text-muted">
              <span>{{ 'hr.emp.branch' | t }}: {{ d.branchName ?? '—' }}</span>
              <span>{{ 'hr.org.manager' | t }}: {{ d.managerName ?? '—' }}</span>
            </div>
            <div class="mt-3 divide-y divide-line">
              @for (s of sectionsOf(d.id); track s.id) {
                <div class="flex items-center gap-2 py-2">
                  <span class="flex-1">{{ s.nameAr }}<span class="muted small"> · {{ s.headName ?? '—' }}</span></span>
                  <span class="badge">{{ s.employeeCount }}</span>
                  @if (auth.canManageHr()) {
                    <button class="btn sm ghost" (click)="openSection(s)">{{ 'common.edit' | t }}</button>
                    <button class="btn sm ghost text-bad" (click)="removeSection(s)">{{ 'common.delete' | t }}</button>
                  }
                </div>
              } @empty { <p class="py-2 text-muted">{{ 'hr.org.noSections' | t }}</p> }
            </div>
          </div>
        </section>
      } @empty { <div class="card-pad muted">{{ 'common.empty' | t }}</div> }
    </div>

    @if (branch(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="branch.set(null)"><div class="modal wide">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('hr.org.newBranch' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'loc.address' | t }}</label><input [(ngModel)]="f.address"></div>

        <app-map-picker [latitude]="f.latitude" [longitude]="f.longitude" [radius]="f.radiusMeters"
                        (picked)="f.latitude = $event.latitude; f.longitude = $event.longitude" />

        <div class="row mt-3">
          <div class="field"><label>{{ 'loc.lat' | t }} *</label><input type="number" step="0.000001" dir="ltr" [(ngModel)]="f.latitude"></div>
          <div class="field"><label>{{ 'loc.lng' | t }} *</label><input type="number" step="0.000001" dir="ltr" [(ngModel)]="f.longitude"></div>
          <button class="btn mb-3" (click)="useMyPosition(f)">{{ 'loc.useMyLocation' | t }}</button>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'loc.radius' | t }} *</label><input type="number" min="20" max="2000" [(ngModel)]="f.radiusMeters"></div>
          <div class="field"><label>{{ 'hr.org.branchCapacity' | t }}</label><input type="number" min="1" max="5000" [(ngModel)]="f.capacity"></div>
        </div>
        <p class="text-xs text-muted">{{ 'hr.org.branchRadiusHint' | t }}</p>

        <div class="modal-foot">
          <button class="btn" (click)="branch.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !branchValid(f)" (click)="saveBranch(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (department(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="department.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('hr.org.newDepartment' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'hr.emp.branch' | t }}</label>
          <select [(ngModel)]="f.branchLocationId"><option [ngValue]="null">—</option>
            @for (b of branches(); track b.id) { <option [value]="b.id">{{ b.nameAr }}</option> }</select></div>
        <div class="field"><label>{{ 'hr.org.manager' | t }}</label>
          <select [(ngModel)]="f.managerId"><option [ngValue]="null">—</option>
            @for (p of people(); track p.id) { <option [value]="p.userId">{{ p.fullName }}</option> }</select></div>
        <div class="modal-foot">
          <button class="btn" (click)="department.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !f.nameAr?.trim() || !f.nameEn?.trim()" (click)="saveDepartment(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (section(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="section.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('hr.org.newSection' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="field"><label>{{ 'hr.org.department' | t }} *</label>
          <select [(ngModel)]="f.departmentId">@for (d of departments(); track d.id) { <option [value]="d.id">{{ d.nameAr }}</option> }</select></div>
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'hr.org.head' | t }}</label>
          <select [(ngModel)]="f.headId"><option [ngValue]="null">—</option>
            @for (p of people(); track p.id) { <option [value]="p.userId">{{ p.fullName }}</option> }</select></div>
        <div class="modal-foot">
          <button class="btn" (click)="section.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !f.nameAr?.trim() || !f.nameEn?.trim() || !f.departmentId" (click)="saveSection(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class HrOrgPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly departments = signal<Department[]>([]);
  readonly sections = signal<Section[]>([]);
  readonly branches = signal<any[]>([]);
  readonly branch = signal<any>(null);
  readonly people = signal<any[]>([]);
  readonly department = signal<any>(null);
  readonly section = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try {
      const [departments, sections, people, branches] = await Promise.all([
        this.api.get<Department[]>('hr/org/departments'),
        this.api.get<Section[]>('hr/org/sections'),
        this.api.get<any[]>('hr/employees'),
        this.api.get<any[]>('locations', { kind: 'Office' }),
      ]);
      this.branches.set(branches);
      this.departments.set(departments);
      this.sections.set(sections);
      this.people.set(people.map(p => ({ ...p, userId: p.userId ?? p.id })));
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  sectionsOf(departmentId: string): Section[] { return this.sections().filter(s => s.departmentId === departmentId); }

  /** Branches are office locations: the same geofence machinery, without a customer code. */
  openBranch(b: any | null): void {
    this.error.set(null);
    this.branch.set(b
      ? { ...b }
      : { id: '', nameAr: '', nameEn: '', address: '', latitude: 25.3462, longitude: 55.4211, radiusMeters: 150, capacity: 500, rowVersion: '' });
  }

  branchValid(f: any): boolean {
    return !!f.nameAr?.trim() && !!f.nameEn?.trim() && Number.isFinite(+f.latitude) && Number.isFinite(+f.longitude)
      && +f.radiusMeters >= 20;
  }

  async useMyPosition(f: any): Promise<void> {
    try {
      const position = await getPosition();
      f.latitude = position.coords.latitude;
      f.longitude = position.coords.longitude;
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async saveBranch(f: any): Promise<void> {
    const body = {
      nameAr: f.nameAr, nameEn: f.nameEn, address: f.address || null,
      latitude: +f.latitude, longitude: +f.longitude, radiusMeters: +f.radiusMeters,
      capacity: +f.capacity || 1, kind: 'Office', rowVersion: f.rowVersion,
    };
    await this.submit(f.id ? this.api.put(`locations/${f.id}`, body) : this.api.post('locations', body),
      () => this.branch.set(null));
  }

  openDepartment(d: Department | null): void {
    this.error.set(null);
    this.department.set(d ? { ...d } : { id: '', nameAr: '', nameEn: '', branchLocationId: null, managerId: null, rowVersion: '' });
  }

  openSection(s: Section | null): void {
    this.error.set(null);
    this.section.set(s ? { ...s } : { id: '', departmentId: this.departments()[0]?.id ?? '', nameAr: '', nameEn: '', headId: null, rowVersion: '' });
  }

  async saveDepartment(f: any): Promise<void> {
    await this.submit(f.id ? this.api.put(`hr/org/departments/${f.id}`, f) : this.api.post('hr/org/departments', f),
      () => this.department.set(null));
  }

  async saveSection(f: any): Promise<void> {
    await this.submit(f.id ? this.api.put(`hr/org/sections/${f.id}`, f) : this.api.post('hr/org/sections', f),
      () => this.section.set(null));
  }

  async removeDepartment(d: Department): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + d.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`hr/org/departments/${d.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async removeSection(s: Section): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + s.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`hr/org/sections/${s.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  private async submit(call: Promise<unknown>, done: () => void): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await call;
      done();
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }
}
