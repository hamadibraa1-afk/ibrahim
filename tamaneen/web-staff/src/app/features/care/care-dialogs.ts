import { inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { LanguageService } from '../../core/i18n/language.service';
import { type ReasonDialogData, type ReasonDialogResult, ReasonDialogComponent } from '../../shared/reason-dialog.component';

/** Dialogs and snack bars shared by the care screens, in the portal's direction. Call from an injection context. */
export function careUi() {
  const dialog = inject(MatDialog);
  const snackBar = inject(MatSnackBar);
  const translate = inject(TranslateService);
  const language = inject(LanguageService);
  return {
    open<T>(data: ReasonDialogData<T>): Observable<ReasonDialogResult<T> | undefined> {
      return dialog
        .open<ReasonDialogComponent<T>, ReasonDialogData<T>, ReasonDialogResult<T>>(ReasonDialogComponent, { data, direction: language.direction(), width: '520px', maxWidth: '95vw', restoreFocus: true })
        .afterClosed();
    },
    /** A confirmation without text: the request runs inside the dialog. */
    confirm<T>(titleKey: string, bodyKey: string, confirmKey: string, danger: boolean, submit: () => Observable<T>): Observable<ReasonDialogResult<T> | undefined> {
      return this.open<T>({ titleKey, bodyKey, labelKey: titleKey, confirmKey, danger, required: false, hideText: true, maxLength: 1, submit: () => submit() });
    },
    notify(key: string): void {
      snackBar.open(translate.instant(key), undefined, { duration: 4000, direction: language.direction(), politeness: 'polite' });
    },
  };
}
