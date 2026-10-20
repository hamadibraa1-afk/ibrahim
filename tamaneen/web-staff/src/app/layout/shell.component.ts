import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { BidiModule } from '@angular/cdk/bidi';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatListModule } from '@angular/material/list';
import { MatSelectModule } from '@angular/material/select';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AuthService } from '../core/auth/auth.service';
import { LanguageService } from '../core/i18n/language.service';
import { SessionService } from '../core/session/session.service';
import type { StaffRole } from '../core/session/staff-role';
import { LanguageToggleComponent } from '../shared/language-toggle.component';

/**
 * Portal frame: toolbar with brand, the signed-in name, the language switch and sign-out; side menu for the active role.
 * [dir] on the root gives every Material component below it the current direction, so the side menu
 * opens from the right in Arabic and from the left in English without extra code.
 */
@Component({
    selector: 'tm-shell',
    imports: [BidiModule, MatButtonModule, MatSidenavModule, MatToolbarModule, MatListModule, MatFormFieldModule, MatSelectModule, RouterOutlet, RouterLink, RouterLinkActive, TranslateModule, LanguageToggleComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './shell.component.html',
    styleUrl: './shell.component.scss'
})
export class ShellComponent {
  protected readonly language = inject(LanguageService);
  protected readonly session = inject(SessionService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly signingOut = signal(false);

  protected switchRole(role: StaffRole): void {
    this.session.switchTo(role);
    void this.router.navigateByUrl(this.session.home());
  }

  protected async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.auth.logout();
    } finally {
      this.signingOut.set(false);
    }
  }
}
