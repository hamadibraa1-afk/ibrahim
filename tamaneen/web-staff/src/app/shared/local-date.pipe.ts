import { Pipe, PipeTransform } from '@angular/core';
import { type DateStyle, formatDate } from '../core/i18n/format';
import type { Language } from '../core/i18n/language';

/** {{ value | tmDate: 'time' : language() }}. Not pure: the S32 time zone can change while a page is open. */
@Pipe({ name: 'tmDate', pure: false })
export class LocalDatePipe implements PipeTransform {
  transform(value: string | null | undefined, style: DateStyle, language: Language): string {
    return formatDate(value, style, language);
  }
}
