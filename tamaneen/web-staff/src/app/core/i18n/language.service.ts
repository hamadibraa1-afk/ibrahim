import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_LANGUAGE, LANGUAGE_STORAGE_KEY, LANGUAGES, type Language, directionOf, otherLanguage, toLanguage } from './language';

/**
 * Owns the portal language. Switching updates the translations, <html lang dir>, and the direction
 * signal that the shell binds to Angular Material's [dir], so every component, menu and dialog
 * flips between RTL and LTR at once. The choice is kept per browser.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>(DEFAULT_LANGUAGE);

  readonly language = this.current.asReadonly();
  readonly direction = computed(() => directionOf(this.current()));
  readonly other = computed(() => otherLanguage(this.current()));

  /** Called once at startup, before the first render, so the page never flashes in the wrong direction. */
  async init(): Promise<void> {
    this.translate.addLangs([...LANGUAGES]);
    this.translate.setDefaultLang(DEFAULT_LANGUAGE);
    await this.use(this.initialLanguage());
  }

  async use(language: Language): Promise<void> {
    await firstValueFrom(this.translate.use(language));
    this.current.set(language);
    const html = this.document.documentElement;
    html.lang = language;
    html.dir = directionOf(language);
    try {
      this.document.defaultView?.localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage can be unavailable (private mode, policy). The choice still applies to this session.
    }
  }

  toggle(): Promise<void> {
    return this.use(this.other());
  }

  private initialLanguage(): Language {
    let stored: string | null = null;
    try {
      stored = this.document.defaultView?.localStorage.getItem(LANGUAGE_STORAGE_KEY) ?? null;
    } catch {
      stored = null;
    }
    return toLanguage(stored) ?? toLanguage(this.document.defaultView?.navigator.language) ?? DEFAULT_LANGUAGE;
  }
}
