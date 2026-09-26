import { Component, OnInit, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { hm, weekday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';

interface Day { date: string; locationNameAr: string; locationNameEn: string; shiftNameAr: string; shiftNameEn: string; start: string; end: string; isOnLeave: boolean; latitude: number; longitude: number; }

@Component({
  selector: 'app-my-schedule',
  standalone: true,
  imports: [TPipe],
  template: `
    <h1>{{ 'nav.mySchedule' | t }}</h1>
    @for (d of days(); track d.date + d.start) {
      <div class="card" style="margin-bottom:10px">
        <div style="display:flex;justify-content:space-between;gap:8px">
          <strong>{{ ('day.' + weekday(d.date)) | t }} <span dir="ltr" class="muted small">{{ d.date }}</span></strong>
          @if (d.isOnLeave) { <span class="badge blue">{{ 'sch.leave' | t }}</span> }
        </div>
        <div>{{ i18n.pick(d.locationNameAr, d.locationNameEn) }} · <span dir="ltr">{{ hm(d.start) }}–{{ hm(d.end) }}</span></div>
        <a class="small" target="_blank" [href]="'https://www.google.com/maps/dir/?api=1&destination=' + d.latitude + ',' + d.longitude">{{ 'common.map' | t }} ↗</a>
      </div>
    } @empty { <div class="card muted">{{ 'common.empty' | t }}</div> }`,
})
export class MySchedulePage implements OnInit {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  readonly hm = hm;
  readonly weekday = weekday;
  readonly days = signal<Day[]>([]);
  async ngOnInit(): Promise<void> { this.days.set(await this.api.get<Day[]>('me/schedule')); }
}
