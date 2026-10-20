import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BidiModule } from '@angular/cdk/bidi';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatListModule } from '@angular/material/list';
import { MatSelectModule } from '@angular/material/select';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { LanguageService } from '../core/i18n/language.service';
import { SessionService } from '../core/session/session.service';
import type { StaffRole } from '../core/session/staff-role';
import { LanguageToggleComponent } from '../shared/language-toggle.component';

/**
 * Portal frame: toolbar with brand and the language switch, side menu for the active role.
 * [dir] on the root gives every Material component below it the current direction, so the side menu
 * opens from the right in Arabic and from the left in English without extra code.
 */
@Component({
  selector: 'tm-shell',
  standalone: true,
  imports: [BidiModule, MatSidenavModule, MatToolbarModule, MatListModule, MatFormFieldModule, MatSelectModule, RouterOutlet, RouterLink, RouterLinkActive, TranslateModule, LanguageToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  protected readonly language = inject(LanguageService);
  protected readonly session = inject(SessionService);

  protected switchRole(role: StaffRole): void {
    this.session.switchTo(role);
  }
}
