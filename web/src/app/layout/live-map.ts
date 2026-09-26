import { AfterViewInit, Component, ElementRef, OnDestroy, effect, input, output, signal, viewChild } from '@angular/core';
import type * as L from 'leaflet';
import { TPipe } from '../core/i18n';

export interface MapSite {
  locationId: string; nameAr: string; nameEn: string; color: string;
  latitude: number; longitude: number; radiusMeters: number; present: number; scheduled: number;
}

/**
 * Live site map for the dashboard: one circle per site, coloured by its current state,
 * with the geofence drawn to scale. Clicking a site raises an event so the page can scroll
 * to it. Leaflet loads lazily and the component hides itself if tiles are unavailable.
 */
@Component({
  selector: 'app-live-map',
  standalone: true,
  imports: [TPipe],
  template: `
    <div class="card overflow-hidden">
      <div class="flex flex-wrap items-center gap-3 border-b border-line px-4 py-3">
        <h2 class="flex-1">{{ 'dash.map' | t }}</h2>
        <span class="flex items-center gap-1.5 text-xs text-muted"><i class="h-2.5 w-2.5 rounded-full bg-ok"></i>{{ 'dash.present' | t }}</span>
        <span class="flex items-center gap-1.5 text-xs text-muted"><i class="h-2.5 w-2.5 rounded-full bg-warn"></i>{{ 'dash.late' | t }}</span>
        <span class="flex items-center gap-1.5 text-xs text-muted"><i class="h-2.5 w-2.5 rounded-full bg-bad"></i>{{ 'dash.uncovered' | t }}</span>
      </div>
      <div #host class="h-[340px] w-full"></div>
      @if (failed()) { <div class="alert red m-4">{{ 'loc.mapOffline' | t }}</div> }
    </div>`,
})
export class LiveMap implements AfterViewInit, OnDestroy {
  readonly sites = input.required<MapSite[]>();
  readonly selected = output<string>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  readonly failed = signal(false);
  private lib?: typeof L;
  private map?: L.Map;
  private layer?: L.LayerGroup;

  constructor() {
    effect(() => {
      const sites = this.sites();
      if (this.map) this.draw(sites);
    });
  }

  async ngAfterViewInit(): Promise<void> {
    try {
      this.lib = await import('leaflet');
      this.map = this.lib.map(this.host().nativeElement, { center: [25.33, 55.42], zoom: 11, scrollWheelZoom: false });
      this.lib.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '© OpenStreetMap' }).addTo(this.map);
      this.layer = this.lib.layerGroup().addTo(this.map);
      this.draw(this.sites());
      setTimeout(() => this.map?.invalidateSize(), 60);
    } catch {
      this.failed.set(true);
    }
  }

  ngOnDestroy(): void { this.map?.remove(); }

  private draw(sites: MapSite[]): void {
    if (!this.lib || !this.map || !this.layer) return;
    this.layer.clearLayers();
    if (!sites.length) return;

    const palette: Record<string, string> = { green: '#15803d', yellow: '#a16207', red: '#b91c1c', grey: '#6b7280' };
    for (const s of sites) {
      const color = palette[s.color] ?? palette['grey'];
      const at: L.LatLngExpression = [s.latitude, s.longitude];

      this.lib.circle(at, { radius: s.radiusMeters, color, weight: 1, fillOpacity: 0.12 }).addTo(this.layer);
      this.lib.circleMarker(at, { radius: 9, color, fillColor: color, fillOpacity: 0.95, weight: 2 })
        .addTo(this.layer)
        .bindTooltip(`${s.nameAr} — ${s.present}/${s.scheduled}`, { direction: 'top' })
        .on('click', () => this.selected.emit(s.locationId));
    }

    const bounds = this.lib.latLngBounds(sites.map(s => [s.latitude, s.longitude] as L.LatLngExpression));
    this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
  }
}
