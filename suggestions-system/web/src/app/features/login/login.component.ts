import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login.component.html',
})
export class LoginComponent implements OnInit {
  private fb = inject(FormBuilder);
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  /** رسالة تظهر عند الوصول لصفحة الدخول بسبب انتهاء الجلسة. */
  sessionExpired = signal(false);
  showPassword = signal(false);
  submitting = signal(false);
  errorMessage = signal<string | null>(null);

  ngOnInit() {
    this.sessionExpired.set(this.route.snapshot.queryParamMap.get('expired') === '1');
  }

  form = this.fb.nonNullable.group({
    userCode: ['', Validators.required],
    password: ['', Validators.required],
  });

  togglePassword() {
    this.showPassword.update(v => !v);
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);

    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => {
        const redirect = this.route.snapshot.queryParamMap.get('redirect');
        this.router.navigateByUrl(redirect || '/my-proposals');
      },
      error: err => {
        this.submitting.set(false);
        this.errorMessage.set(err?.error?.message ?? 'تعذّر تسجيل الدخول. تحقّق من البيانات وحاول مجدداً.');
      },
    });
  }
}
