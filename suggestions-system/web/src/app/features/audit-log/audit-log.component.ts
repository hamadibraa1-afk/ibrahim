import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuditService } from '../../core/services/audit.service';
import { UserService } from '../../core/services/user.service';
import { AuditActionLabels, AuditActionStyles, AuditLog } from '../../core/models/audit.model';
import { User } from '../../core/models/user.model';

/** السجل الكامل لكل إجراءات النظام — لمدير النظام، مع فلاتر بحث. */
@Component({
  selector: 'app-audit-log',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './audit-log.component.html',
})
export class AuditLogComponent implements OnInit {
  private auditService = inject(AuditService);
  private userService = inject(UserService);

  readonly AuditActionLabels = AuditActionLabels;
  readonly actionOptions = Object.keys(AuditActionLabels);

  logs = signal<AuditLog[]>([]);
  users = signal<User[]>([]);
  loading = signal(true);

  search = '';
  action = '';
  actorId: number | null = null;
  from = '';
  to = '';

  private debounce?: ReturnType<typeof setTimeout>;

  ngOnInit() {
    this.userService.getAll().subscribe(u => this.users.set(u));
    this.load();
  }

  onSearchInput() {
    if (this.debounce) clearTimeout(this.debounce);
    this.debounce = setTimeout(() => this.load(), 350);
  }

  load() {
    this.loading.set(true);
    this.auditService.all({
      search: this.search || undefined,
      action: this.action || undefined,
      actorId: this.actorId ?? undefined,
      from: this.from || undefined,
      to: this.to || undefined,
      take: 300,
    }).subscribe({
      next: d => { this.logs.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  reset() {
    this.search = ''; this.action = ''; this.actorId = null; this.from = ''; this.to = '';
    this.load();
  }

  labelFor(a: string) { return AuditActionLabels[a] ?? a; }
  styleFor(a: string) { return AuditActionStyles[a] ?? 'bg-gray-100 text-gray-600'; }
}
