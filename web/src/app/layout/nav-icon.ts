import { Component, input } from '@angular/core';

/** Small inline icon set — no icon font, no extra request. */
@Component({
  selector: 'app-icon',
  standalone: true,
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"
         stroke-linejoin="round" class="h-[18px] w-[18px] shrink-0">
      @switch (name()) {
        @case ('dashboard') { <path d="M4 13h6V4H4v9Zm10 7h6v-9h-6v9ZM4 20h6v-4H4v4Zm10-11h6V4h-6v5Z" /> }
        @case ('schedule') { <rect x="3" y="5" width="18" height="16" rx="2" /><path d="M8 3v4M16 3v4M3 11h18" /> }
        @case ('attendance') { <circle cx="12" cy="12" r="9" /><path d="M12 7.5V12l3 2" /> }
        @case ('requests') { <path d="M7 4h10a2 2 0 0 1 2 2v14l-7-3-7 3V6a2 2 0 0 1 2-2Z" /> }
        @case ('ratings') { <path d="m12 4 2.4 4.9 5.4.8-3.9 3.8.9 5.4-4.8-2.6-4.8 2.6.9-5.4L4.2 9.7l5.4-.8L12 4Z" /> }
        @case ('people') { <circle cx="9" cy="8" r="3.2" /><path d="M3.5 19a5.5 5.5 0 0 1 11 0M17 11.5a2.7 2.7 0 1 0 0-5.4M18 19a4.6 4.6 0 0 0-2.3-4" /> }
        @case ('locations') { <path d="M12 21s7-5.3 7-11a7 7 0 1 0-14 0c0 5.7 7 11 7 11Z" /><circle cx="12" cy="10" r="2.6" /> }
        @case ('shifts') { <path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6 7.8 7.8M16.2 16.2l2.2 2.2M18.4 5.6l-2.2 2.2M7.8 16.2l-2.2 2.2" /> }
        @case ('feedback') { <path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12Z" /><path d="M9 10h6M9 14h4" /> }
        @case ('allowances') { <rect x="3" y="6" width="18" height="12" rx="2" /><circle cx="12" cy="12" r="2.6" /><path d="M6.5 12h.01M17.5 12h.01" /> }
        @case ('today') { <path d="M4 7h16M4 12h16M4 17h9" /> }
        @case ('logout') { <path d="M15 17v1.5A2.5 2.5 0 0 1 12.5 21h-6A2.5 2.5 0 0 1 4 18.5v-13A2.5 2.5 0 0 1 6.5 3h6A2.5 2.5 0 0 1 15 5.5V7M19 12H9m10 0-3-3m3 3-3 3" /> }
      }
    </svg>`,
})
export class Icon {
  readonly name = input.required<string>();
}
