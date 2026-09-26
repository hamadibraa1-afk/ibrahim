import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Row {
  id: string; fullName: string; employeeNumber: string; role: string; phone: string | null;
  departmentId: string; departmentName: string; sectionName: string | null; jobTitleName: string | null;
  branchName: string; managerName: string | null; hireDate: string; status: string; scheduleName: string | null;
}
interface Detail extends Record<string, any> { row: Row; }
interface Named { id: string; nameAr: string; nameEn: string; }
interface SalaryRow { oldSalary: number; newSalary: number; effectiveFrom: string; reason: string; decidedByName: string | null; }

@Component({
  selector: 'app-hr-employees',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.employees' | t }}</h1><span class="spacer"></span>
      @if (auth.canManageHr()) { <button class="btn primary" (click)="openNew()">+ {{ 'hr.emp.new' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    <div class="card-pad mb-3"><div class="row">
      <div class="field"><label>{{ 'common.search' | t }}</label><input [(ngModel)]="q"></div>
      <div class="field"><label>{{ 'hr.org.department' | t }}</label>
        <select [(ngModel)]="departmentId" (change)="load()"><option value="">{{ 'common.all' | t }}</option>
          @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.nameAr }}</option> }</select></div>
      <div class="field"><label>{{ 'common.status' | t }}</label>
        <select [(ngModel)]="status" (change)="load()"><option value="">{{ 'common.all' | t }}</option>
          @for (s of statuses; track s) { <option [value]="s">{{ 'hr.status.' + s | t }}</option> }</select></div>
      <span class="flex-1"></span><span class="muted small mb-3">{{ filtered().length }}</span>
    </div></div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'emp.number' | t }}</th><th>{{ 'common.name' | t }}</th><th>{{ 'hr.emp.jobTitle' | t }}</th>
        <th>{{ 'hr.org.department' | t }}</th><th>{{ 'hr.emp.branch' | t }}</th><th>{{ 'hr.emp.manager' | t }}</th>
        <th>{{ 'hr.emp.schedule' | t }}</th><th>{{ 'common.status' | t }}</th><th></th></tr></thead>
      <tbody>
        @for (r of filtered(); track r.id) {
          <tr [class.inactive]="r.status === 'Ended'">
            <td dir="ltr" class="tabular">{{ r.employeeNumber }}</td>
            <td>{{ r.fullName }}<div class="muted small">{{ 'role.' + r.role | t }}</div></td>
            <td>{{ r.jobTitleName ?? '—' }}</td>
            <td>{{ r.departmentName }}<div class="muted small">{{ r.sectionName }}</div></td>
            <td>{{ r.branchName }}</td><td>{{ r.managerName ?? '—' }}</td><td>{{ r.scheduleName ?? '—' }}</td>
            <td><span class="badge" [class.green]="r.status === 'Active'" [class.yellow]="r.status === 'Suspended'"
                      [class.red]="r.status === 'Ended'">{{ 'hr.status.' + r.status | t }}</span></td>
            <td><button class="btn sm" (click)="open(r)">{{ 'hr.emp.profile' | t }}</button></td>
          </tr>
        } @empty { <tr><td colspan="9" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>

    @if (detail(); as d) {
      <div class="modal-back" appBackdrop (dismiss)="detail.set(null)"><div class="modal wide">
        <div class="modal-head"><h2>{{ d.row.fullName }} <span class="muted small" dir="ltr">{{ d.row.employeeNumber }}</span></h2>
          <button class="btn sm" (click)="detail.set(null)">{{ 'common.close' | t }}</button></div>

        <div class="tabs">
          @for (t of tabs; track t.key) { <button [class.active]="tab() === t.key" (click)="setTab(t.key, d)">{{ t.label | t }}</button> }
        </div>

        @if (error()) { <div class="alert red">{{ error() }}</div> }

        @if (tab() === 'data') {
          <div class="row">
            <div class="field"><label>{{ 'common.name' | t }} *</label><input [(ngModel)]="d['fullName']"></div>
            <div class="field"><label>{{ 'emp.phone' | t }} *</label><input dir="ltr" [(ngModel)]="d['phone']"></div>
            <div class="field"><label>{{ 'emp.email' | t }}</label><input dir="ltr" [(ngModel)]="d['email']"></div>
          </div>
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.branch' | t }} *</label>
              <select [(ngModel)]="d['branchLocationId']">@for (b of branches(); track b.id) { <option [value]="b.id">{{ b.nameAr }}</option> }</select></div>
            <div class="field"><label>{{ 'hr.org.department' | t }} *</label>
              <select [(ngModel)]="d['departmentId']" (change)="d['sectionId'] = null">
                @for (x of departments(); track x.id) { <option [value]="x.id">{{ x.nameAr }}</option> }</select></div>
            <div class="field"><label>{{ 'hr.org.section' | t }}</label>
              <select [(ngModel)]="d['sectionId']"><option [ngValue]="null">—</option>
                @for (s of sectionsOf(d['departmentId']); track s.id) { <option [value]="s.id">{{ s.nameAr }}</option> }</select></div>
          </div>
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.jobTitle' | t }}</label>
              <select [(ngModel)]="d['jobTitleId']"><option [ngValue]="null">—</option>
                @for (j of jobTitles(); track j.id) { <option [value]="j.id">{{ j.nameAr }}</option> }</select></div>
            <div class="field"><label>{{ 'hr.emp.grade' | t }}</label>
              <select [(ngModel)]="d['gradeId']"><option [ngValue]="null">—</option>
                @for (g of grades(); track g.id) { <option [value]="g.id">{{ g.nameAr }}</option> }</select></div>
            <div class="field"><label>{{ 'hr.emp.contract' | t }}</label>
              <select [(ngModel)]="d['contractTypeId']"><option [ngValue]="null">—</option>
                @for (c of contracts(); track c.id) { <option [value]="c.id">{{ c.nameAr }}</option> }</select></div>
          </div>
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.manager' | t }}</label>
              <select [(ngModel)]="d['managerId']"><option [ngValue]="null">—</option>
                @for (m of managers(d.row.id); track m.id) { <option [value]="m.userId ?? m.id">{{ m.fullName }}</option> }</select></div>
            <div class="field"><label>{{ 'hr.emp.hireDate' | t }} *</label><input type="date" [(ngModel)]="d['hireDate']"></div>
            <div class="field"><label>{{ 'hr.emp.nationality' | t }}</label><input [(ngModel)]="d['nationality']"></div>
          </div>
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.id' | t }}</label><input dir="ltr" [(ngModel)]="d['idNumber']"></div>
            <div class="field"><label>{{ 'hr.emp.idExpiry' | t }}</label><input type="date" [(ngModel)]="d['idExpiry']"></div>
            <div class="field"><label>{{ 'hr.emp.passport' | t }}</label><input dir="ltr" [(ngModel)]="d['passportNumber']"></div>
            <div class="field"><label>{{ 'hr.emp.passportExpiry' | t }}</label><input type="date" [(ngModel)]="d['passportExpiry']"></div>
          </div>
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.residency' | t }}</label><input dir="ltr" [(ngModel)]="d['residencyNumber']"></div>
            <div class="field"><label>{{ 'hr.emp.residencyExpiry' | t }}</label><input type="date" [(ngModel)]="d['residencyExpiry']"></div>
            <div class="field"><label>{{ 'hr.emp.iban' | t }}</label><input dir="ltr" [(ngModel)]="d['iban']"></div>
          </div>
          @if (auth.canManageHr()) {
            <div class="modal-foot">
              <button class="btn danger" (click)="endService(d)">{{ 'hr.emp.endService' | t }}</button>
              <span class="flex-1"></span>
              <button class="btn" (click)="detail.set(null)">{{ 'common.cancel' | t }}</button>
              <button class="btn primary" [disabled]="busy()" (click)="save(d)">{{ 'common.save' | t }}</button>
            </div>
          }
        }

        @if (tab() === 'schedule') {
          <div class="row">
            <div class="field"><label>{{ 'hr.emp.schedule' | t }}</label>
              <select [(ngModel)]="scheduleId"><option [ngValue]="null">—</option>
                @for (s of schedules(); track s.id) { <option [value]="s.id">{{ s.nameAr }}</option> }</select></div>
            <div class="field"><label>{{ 'sch.effective' | t }}</label><input type="date" [(ngModel)]="effectiveFrom"></div>
            @if (auth.canManageHr()) { <button class="btn primary mb-3" [disabled]="busy()" (click)="saveSchedule(d)">{{ 'common.save' | t }}</button> }
          </div>
          <p class="text-sm text-muted">{{ 'hr.emp.scheduleHint' | t }}</p>
        }

        @if (tab() === 'salary') {
          @if (d['basicSalary'] === null) { <div class="alert">{{ 'hr.emp.salaryHidden' | t }}</div> }
          @else {
            <div class="rounded-xl bg-raised p-4 mb-4">
              <div class="text-xs text-muted">{{ 'hr.emp.currentSalary' | t }}</div>
              <div class="text-2xl font-bold tabular">{{ d['basicSalary'] }}</div>
            </div>
            @if (auth.canManageHr()) {
              <div class="row">
                <div class="field"><label>{{ 'hr.emp.newSalary' | t }}</label><input type="number" min="0" step="50" [(ngModel)]="newSalary"></div>
                <div class="field"><label>{{ 'sch.effective' | t }}</label><input type="date" [(ngModel)]="effectiveFrom"></div>
                <div class="field"><label>{{ 'common.reason' | t }}</label><input [(ngModel)]="salaryReason"></div>
                <button class="btn primary mb-3" [disabled]="busy() || !salaryReason.trim()" (click)="saveSalary(d)">{{ 'common.save' | t }}</button>
              </div>
            }
            <div class="table-wrap"><table>
              <thead><tr><th>{{ 'sch.effective' | t }}</th><th>{{ 'hr.emp.from' | t }}</th><th>{{ 'hr.emp.to' | t }}</th>
                <th>{{ 'common.reason' | t }}</th><th>{{ 'req.decidedBy' | t }}</th></tr></thead>
              <tbody>@for (h of history(); track h.effectiveFrom + h.newSalary) {
                <tr><td dir="ltr">{{ h.effectiveFrom }}</td><td class="tabular">{{ h.oldSalary }}</td>
                  <td class="tabular font-semibold">{{ h.newSalary }}</td><td>{{ h.reason }}</td><td>{{ h.decidedByName }}</td></tr>
              } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }</tbody></table></div>
          }
        }
      </div></div>
    }

    @if (creating(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="creating.set(null)"><div class="modal wide">
        <div class="modal-head"><h2>{{ 'hr.emp.new' | t }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'common.name' | t }} *</label><input [(ngModel)]="f.fullName"></div>
          <div class="field"><label>{{ 'emp.number' | t }} *</label><input dir="ltr" [(ngModel)]="f.employeeNumber"></div>
          <div class="field"><label>{{ 'emp.phone' | t }} *</label><input dir="ltr" [(ngModel)]="f.phone"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'role.title' | t }} *</label>
            <select [(ngModel)]="f.role">@for (r of officeRoles; track r) { <option [value]="r">{{ 'role.' + r | t }}</option> }</select></div>
          <div class="field"><label>{{ 'emp.password' | t }} *</label><input dir="ltr" [(ngModel)]="f.password"></div>
          <div class="field"><label>{{ 'hr.emp.hireDate' | t }} *</label><input type="date" [(ngModel)]="f.hireDate"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'hr.emp.branch' | t }} *</label>
            <select [(ngModel)]="f.branchLocationId">@for (b of branches(); track b.id) { <option [value]="b.id">{{ b.nameAr }}</option> }</select></div>
          <div class="field"><label>{{ 'hr.org.department' | t }} *</label>
            <select [(ngModel)]="f.departmentId">@for (x of departments(); track x.id) { <option [value]="x.id">{{ x.nameAr }}</option> }</select></div>
          <div class="field"><label>{{ 'hr.emp.schedule' | t }}</label>
            <select [(ngModel)]="f.workScheduleId"><option [ngValue]="null">—</option>
              @for (s of schedules(); track s.id) { <option [value]="s.id">{{ s.nameAr }}</option> }</select></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'hr.emp.jobTitle' | t }}</label>
            <select [(ngModel)]="f.jobTitleId"><option [ngValue]="null">—</option>
              @for (j of jobTitles(); track j.id) { <option [value]="j.id">{{ j.nameAr }}</option> }</select></div>
          <div class="field"><label>{{ 'hr.emp.salary' | t }}</label><input type="number" min="0" step="50" [(ngModel)]="f.basicSalary"></div>
        </div>
        <div class="modal-foot">
          <button class="btn" (click)="creating.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !newValid(f)" (click)="create(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class HrEmployeesPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly rows = signal<Row[]>([]);
  readonly departments = signal<any[]>([]);
  readonly sections = signal<any[]>([]);
  readonly branches = signal<Named[]>([]);
  readonly jobTitles = signal<Named[]>([]);
  readonly grades = signal<Named[]>([]);
  readonly contracts = signal<Named[]>([]);
  readonly schedules = signal<Named[]>([]);
  readonly history = signal<SalaryRow[]>([]);
  readonly detail = signal<Detail | null>(null);
  readonly creating = signal<any>(null);
  readonly tab = signal('data');
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly statuses = ['Active', 'Suspended', 'Ended'];
  readonly officeRoles = ['Employee', 'DepartmentManager', 'HrOfficer', 'HrManager'];
  readonly tabs = [
    { key: 'data', label: 'hr.emp.tabData' },
    { key: 'schedule', label: 'hr.emp.tabSchedule' },
    { key: 'salary', label: 'hr.emp.tabSalary' },
  ];
  q = '';
  departmentId = '';
  status = '';
  scheduleId: string | null = null;
  effectiveFrom = uaeToday();
  newSalary = 0;
  salaryReason = '';

  readonly filtered = computed(() => {
    const q = this.q.trim().toLowerCase();
    return this.rows().filter(r => !q || [r.fullName, r.employeeNumber, r.jobTitleName ?? '', r.departmentName].some(v => v.toLowerCase().includes(q)));
  });

  async ngOnInit(): Promise<void> {
    const [departments, sections, branches, jobTitles, grades, contracts, schedules] = await Promise.all([
      this.api.get<any[]>('hr/org/departments'),
      this.api.get<any[]>('hr/org/sections'),
      this.api.get<Named[]>('locations', { kind: 'Office' }),
      this.api.get<Named[]>('hr/lookups/job-titles'),
      this.api.get<Named[]>('hr/lookups/grades'),
      this.api.get<Named[]>('hr/lookups/contract-types'),
      this.api.get<Named[]>('hr/work-schedules'),
    ]);
    this.departments.set(departments);
    this.sections.set(sections);
    this.branches.set(branches);
    this.jobTitles.set(jobTitles);
    this.grades.set(grades);
    this.contracts.set(contracts);
    this.schedules.set(schedules);
    await this.load();
  }

  async load(): Promise<void> {
    try { this.rows.set(await this.api.get<Row[]>('hr/employees', { departmentId: this.departmentId, status: this.status })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  sectionsOf(departmentId: string): any[] { return this.sections().filter(s => s.departmentId === departmentId); }

  managers(currentId: string): any[] {
    return this.rows().filter(r => r.id !== currentId && r.status !== 'Ended').map(r => ({ id: r.id, userId: (r as any).userId, fullName: r.fullName }));
  }

  async open(r: Row): Promise<void> {
    this.error.set(null);
    this.tab.set('data');
    const detail = await this.api.get<any>(`hr/employees/${r.id}`);
    this.detail.set({ ...detail, ...detail.row, row: detail.row });
    this.scheduleId = detail.workScheduleId ?? null;
    this.effectiveFrom = uaeToday();
    this.newSalary = detail.basicSalary ?? 0;
    this.salaryReason = '';
  }

  async setTab(key: string, d: Detail): Promise<void> {
    this.tab.set(key);
    if (key === 'salary' && this.auth.canManageHr()) {
      try { this.history.set(await this.api.get<SalaryRow[]>(`hr/employees/${d.row.id}/salary-history`)); }
      catch { this.history.set([]); }
    }
  }

  openNew(): void {
    this.error.set(null);
    this.creating.set({
      fullName: '', employeeNumber: '', phone: '', email: '', role: 'Employee', preferredLanguage: 'ar', password: '',
      branchLocationId: this.branches()[0]?.id ?? '', departmentId: this.departments()[0]?.id ?? '', sectionId: null,
      jobTitleId: null, gradeId: null, contractTypeId: null, managerId: null,
      workScheduleId: this.schedules()[0]?.id ?? null, hireDate: uaeToday(), basicSalary: 0,
    });
  }

  newValid(f: any): boolean {
    return !!f.fullName?.trim() && !!f.employeeNumber?.trim() && !!f.phone?.trim()
      && f.password?.length >= 8 && !!f.branchLocationId && !!f.departmentId;
  }

  async create(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post('hr/employees', { ...f, email: f.email || null, basicSalary: +f.basicSalary || 0 });
      this.creating.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async save(d: Detail): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.put(`hr/employees/${d.row.id}`, {
        fullName: d['fullName'], phone: d['phone'], email: d['email'] || null, preferredLanguage: 'ar',
        branchLocationId: d['branchLocationId'], departmentId: d['departmentId'], sectionId: d['sectionId'] || null,
        jobTitleId: d['jobTitleId'] || null, gradeId: d['gradeId'] || null, contractTypeId: d['contractTypeId'] || null,
        managerId: d['managerId'] || null, hireDate: d['hireDate'], nationality: d['nationality'] || null,
        birthDate: d['birthDate'] || null, emergencyContactName: d['emergencyContactName'] || null,
        emergencyContactPhone: d['emergencyContactPhone'] || null, notes: d['notes'] || null,
        idNumber: d['idNumber'] || null, idExpiry: d['idExpiry'] || null,
        passportNumber: d['passportNumber'] || null, passportExpiry: d['passportExpiry'] || null,
        residencyNumber: d['residencyNumber'] || null, residencyExpiry: d['residencyExpiry'] || null,
        iban: d['iban'] || null, rowVersion: d['rowVersion'],
      });
      this.detail.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async saveSchedule(d: Detail): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post(`hr/employees/${d.row.id}/schedule`, { workScheduleId: this.scheduleId, effectiveFrom: this.effectiveFrom });
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async saveSalary(d: Detail): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post(`hr/employees/${d.row.id}/salary`, { newSalary: +this.newSalary, effectiveFrom: this.effectiveFrom, reason: this.salaryReason });
      this.ui.ok(this.i18n.t('common.saved'));
      await this.open(d.row);
      await this.setTab('salary', d);
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async endService(d: Detail): Promise<void> {
    const reason = await this.ui.prompt(this.i18n.t('hr.emp.endService'), this.i18n.t('common.reason'));
    if (!reason) return;
    try {
      await this.api.post(`hr/employees/${d.row.id}/end-service`, { endDate: uaeToday(), reason });
      this.detail.set(null);
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
