import { Pipe, PipeTransform } from '@angular/core';
import { type DateStyle, formatDate } from '../core/i18n/format';
import type { Language } from '../core/i18n/language';

/** {{ value | tmDate: 'time' : language() }}: pure, re-runs when the language argument changes. */
@Pipe({ name: 'tmDate' })
export class LocalDatePipe implements PipeTransform {
  transform(value: string | null | undefined, style: DateStyle, language: Language): string {
    return formatDate(value, style, language);
  }
}
