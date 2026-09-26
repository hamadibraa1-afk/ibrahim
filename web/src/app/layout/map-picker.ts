import { AfterViewInit, Component, ElementRef, OnDestroy, effect, input, output, signal, viewChild } from '@angular/core';
import type * as L from 'leaflet';
import { TPipe } from '../core/i18n';

/**
 * Click-to-place site picker. Leaflet is imported lazily so the map only costs
 * anything on the locations screen, and the component degrades to a message
 * (with manual coordinates still editable in the parent form) if tiles cannot load.
 */
@Component({
  selector: 'app-map-picker',
  standalone: true,
  imports: [TPipe],
  styles: [`
    .map { height: 280px; border-radius: 10px; border: 1px solid var(--border); overflow: hidden; }
    .hint { font-size: .8rem; color: var(--muted); margin-top: 6px; }
  `],
  template: `
    <div class="map" #host></div>
    @if (failed()) { <div class="alert">{{ 'loc.mapOffline' | t }}</div> }
    @else { <div class="hint">{{ 'loc.mapHint' | t }}</div> }`,
})
export class MapPicker implements AfterViewInit, OnDestroy {
  readonly latitude = input<number | null>(null);
  readonly longitude = input<number | null>(null);
  readonly radius = input<number>(100);
  readonly picked = output<{ latitude: number; longitude: number }>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  readonly failed = signal(false);
  private map?: L.Map;
  private marker?: L.CircleMarker;
  private circle?: L.Circle;
  private lib?: typeof L;

  constructor() {
    effect(() => {
      const lat = this.latitude();
      const lng = this.longitude();
      const r = this.radius();
      if (this.map && lat !== null && lng !== null) this.draw(lat, lng, r, false);
    });
  }

  async ngAfterViewInit(): Promise<void> {
    try {
      this.lib = await import('leaflet');
      const lat = this.latitude() ?? 25.3463;
      const lng = this.longitude() ?? 55.4209;
      this.map = this.lib.map(this.host().nativeElement, { center: [lat, lng], zoom: this.latitude() === null ? 11 : 16 });
      this.lib.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '© OpenStreetMap' }).addTo(this.map);
      this.map.on('click', (e: L.LeafletMouseEvent) => {
        const point = { latitude: +e.latlng.lat.toFixed(6), longitude: +e.latlng.lng.toFixed(6) };
        this.draw(point.latitude, point.longitude, this.radius(), false);
        this.picked.emit(point);
      });
      if (this.latitude() !== null && this.longitude() !== null) this.draw(this.latitude()!, this.longitude()!, this.radius(), true);
      setTimeout(() => this.map?.invalidateSize(), 60);
    } catch {
      this.failed.set(true);
    }
  }

  ngOnDestroy(): void { this.map?.remove(); }

  private draw(lat: number, lng: number, radius: number, center: boolean): void {
    if (!this.lib || !this.map) return;
    const at: L.LatLngExpression = [lat, lng];
    this.marker ??= this.lib.circleMarker(at, { radius: 6, color: '#0f766e', fillColor: '#0f766e', fillOpacity: 1 }).addTo(this.map);
    this.circle ??= this.lib.circle(at, { radius, color: '#0f766e', weight: 1, fillOpacity: 0.12 }).addTo(this.map);
    this.marker.setLatLng(at);
    this.circle.setLatLng(at);
    this.circle.setRadius(radius);
    if (center) this.map.setView(at, 16);
  }
}
