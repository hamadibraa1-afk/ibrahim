import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { UserService } from '../../core/services/user.service';
import { DepartmentService } from '../../core/services/department.service';
import { RouterLink } from '@angular/router';
import { User, UserRole, UserRoleLabels, UserStatus } from '../../core/models/user.model';

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, RouterLink],
  templateUrl: './user-management.component.html',
})
export class UserManagementComponent implements OnInit {
  private fb = inject(FormBuilder);
  private userService = inject(UserService);
  private departmentService = inject(DepartmentService);

  /** الإدارات المفعّلة من الإعدادات — مصدر القائمة المنسدلة. */
  departments: string[] = [];

  readonly UserRole = UserRole;
  readonly UserStatus = UserStatus;
  readonly UserRoleLabels = UserRoleLabels;
  readonly roleOptions = Object.values(UserRole);

  users: User[] = [];
  loading = true;
  saving = false;
  searchTerm = '';

  showModal = false;
  editingUser: User | null = null;

  form = this.fb.nonNullable.group({
    userCode: ['', Validators.required],
    arabicName: ['', Validators.required],
    englishName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    initialPassword: [''],
    role: [UserRole.Employee, Validators.required],
    phoneNumber: ['', Validators.required],
    department: ['', Validators.required],
    jobTitle: ['', Validators.required],
    /** المدير المباشر — مسار التصعيد التلقائي عند تجاوز المهل. */
    managerId: [null as number | null],
  });

  /** المرشّحون لمنصب المدير المباشر: كل المستخدمين النشطين عدا المستخدم نفسه. */
  get managerOptions(): User[] {
    return this.users.filter(u => u.id !== this.editingUser?.id && u.status === UserStatus.Active);
  }

  ngOnInit() {
    this.load();
    this.departmentService.getAll(true).subscribe({
      next: d => (this.departments = d.map(x => x.name)),
      error: () => {},
    });
  }

  /**
   * خيارات القائمة: الإدارات المفعّلة، مع إدارة المستخدم الحالية إن كانت قد أُوقفت بعد تعيينه،
   * حتى يُحفظ تعديله دون إجباره على تغيير إدارته.
   */
  get departmentOptions(): string[] {
    const current = this.editingUser?.department;
    return current && !this.departments.includes(current) ? [current, ...this.departments] : this.departments;
  }

  load() {
    this.loading = true;
    this.userService.getAll().subscribe({
      next: users => { this.users = users; this.loading = false; },
      error: () => (this.loading = false),
    });
  }

  get filteredUsers(): User[] {
    const term = this.searchTerm.trim();
    if (!term) return this.users;
    return this.users.filter(u =>
      u.arabicName.includes(term) ||
      u.englishName.toLowerCase().includes(term.toLowerCase()) ||
      u.userCode.toLowerCase().includes(term.toLowerCase()) ||
      u.department.includes(term)
    );
  }

  openAddModal() {
    this.editingUser = null;
    this.form.reset({
      userCode: '', arabicName: '', englishName: '', email: '', initialPassword: '',
      role: UserRole.Employee, phoneNumber: '', department: '', jobTitle: '', managerId: null,
    });
    // كلمة المرور الابتدائية إلزامية عند الإضافة فقط
    this.form.controls.initialPassword.setValidators([Validators.required, Validators.minLength(8)]);
    this.form.controls.initialPassword.updateValueAndValidity();
    this.showModal = true;
  }

  openEditModal(user: User) {
    this.editingUser = user;
    this.form.reset({
      userCode: user.userCode,
      arabicName: user.arabicName,
      englishName: user.englishName,
      email: user.email,
      initialPassword: '',
      role: user.role,
      phoneNumber: user.phoneNumber,
      department: user.department,
      jobTitle: user.jobTitle,
      managerId: user.managerId,
    });
    // لا تُطلب كلمة مرور عند التعديل — تُغيَّر عبر زر "إعادة تعيين كلمة المرور"
    this.form.controls.initialPassword.clearValidators();
    this.form.controls.initialPassword.updateValueAndValidity();
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.editingUser = null;
  }

  errorMessage: string | null = null;

  resetPassword(user: User) {
    const pwd = prompt(`كلمة المرور الجديدة للمستخدم "${user.arabicName}" (8 أحرف على الأقل):`);
    if (!pwd) return;
    if (pwd.length < 8) { alert('كلمة المرور يجب ألا تقل عن 8 أحرف.'); return; }
    this.userService.resetPassword(user.id, pwd).subscribe({
      next: () => alert('تم تغيير كلمة المرور بنجاح.'),
      error: err => alert(err?.error?.message ?? 'تعذّر تغيير كلمة المرور.'),
    });
  }

  save() {
    this.errorMessage = null;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving = true;
    const v = this.form.getRawValue();

    const request$ = this.editingUser
      ? this.userService.update({
          id: this.editingUser.id,
          userCode: v.userCode, arabicName: v.arabicName, englishName: v.englishName,
          email: v.email, role: v.role, phoneNumber: v.phoneNumber,
          department: v.department, jobTitle: v.jobTitle, managerId: v.managerId,
        })
      : this.userService.create(v);

    request$.subscribe({
      next: () => { this.saving = false; this.closeModal(); this.load(); },
      error: err => { this.saving = false; this.errorMessage = err?.error?.message ?? 'تعذّر حفظ البيانات.'; },
    });
  }

  suspendOrActivate(user: User) {
    const action = user.status === UserStatus.Active
      ? this.userService.suspend(user.id)
      : this.userService.activate(user.id);
    action.subscribe(() => this.load());
  }

  deleteUser(user: User) {
    if (!confirm(`هل تريد حذف المستخدم "${user.arabicName}"؟ لا يمكن التراجع عن هذا الإجراء.`)) return;
    this.userService.delete(user.id).subscribe(() => this.load());
  }
}
