import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/services/auth.service';
import { UserRoleLabels, UserStatus } from '../../core/models/user.model';

@Component({
  selector: 'app-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './profile.component.html',
})
export class ProfileComponent implements OnInit {
  private fb = inject(FormBuilder);
  auth = inject(AuthService);

  readonly UserRoleLabels = UserRoleLabels;
  readonly UserStatus = UserStatus;
  readonly user = this.auth.currentUser;

  showCurrent = signal(false);
  showNew = signal(false);
  saving = signal(false);
  successMessage = signal<string | null>(null);
  errorMessage = signal<string | null>(null);

  form = this.fb.nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
  });

  ngOnInit() {
    // نحدّث البيانات من الخادم في حال عدّلها مدير النظام
    this.auth.refreshMe().subscribe({ error: () => {} });
  }

  get passwordsMismatch(): boolean {
    const { newPassword, confirmPassword } = this.form.getRawValue();
    return !!confirmPassword && newPassword !== confirmPassword;
  }

  initials(name: string): string {
    return name.trim().split(/\s+/).slice(0, 2).map(p => p.charAt(0)).join('');
  }

  submit() {
    this.successMessage.set(null);
    this.errorMessage.set(null);

    if (this.form.invalid || this.passwordsMismatch) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    const { currentPassword, newPassword } = this.form.getRawValue();

    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.saving.set(false);
        this.form.reset();
        this.successMessage.set('تم تغيير كلمة المرور بنجاح.');
      },
      error: err => {
        this.saving.set(false);
        this.errorMessage.set(err?.error?.message ?? 'تعذّر تغيير كلمة المرور.');
      },
    });
  }
}
