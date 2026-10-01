import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProposalStatus, ProposalStatusBadgeClass, ProposalStatusLabels } from '../models/proposal.model';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [CommonModule],
  template: `<span class="badge" [ngClass]="cls">{{ label }}</span>`,
})
export class StatusBadgeComponent {
  @Input({ required: true }) status!: ProposalStatus;
  get label() { return ProposalStatusLabels[this.status] ?? this.status; }
  get cls() { return ProposalStatusBadgeClass[this.status] ?? 'badge-gray'; }
}
