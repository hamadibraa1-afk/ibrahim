import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule, Location } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { ProposalService } from '../../core/services/proposal.service';
import { UserService } from '../../core/services/user.service';
import { AuthService } from '../../core/services/auth.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { FormSettingsService } from '../../core/services/form-settings.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import { AuditTimelineComponent } from '../../core/components/audit-timeline.component';
import { StatusBadgeComponent } from '../../core/components/status-badge.component';
import { ImpactPanelComponent } from './impact-panel.component';
import {
  BlindReviewPolicy, ClassificationLabels, selectedImpacts, NotApplicableReason, NotApplicableReasonLabels,
  Proposal, ProposalClassification, ProposalStatus,
} from '../../core/models/proposal.model';
import { FormField } from '../../core/models/form-field.model';
import { User, UserRole } from '../../core/models/user.model';

/**
 * صفحة تفاصيل مقترح واحد — نقطة العمل المركزية.
 * الإجراءات المتاحة يحسبها الخادم (p.actions) حسب الدور والحالة والتصعيد،
 * والصفحة تتحدّث لحظياً عند أي انتقال للمقترح عبر SignalR.
 */
@Component({
  selector: 'app-proposal-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AttachmentListComponent, AuditTimelineComponent, StatusBadgeComponent, ImpactPanelComponent],
  templateUrl: './proposal-detail.component.html',
})
export class ProposalDetailComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private location = inject(Location);
  private proposalService = inject(ProposalService);
  private userService = inject(UserService);
  private formSettings = inject(FormSettingsService);
  private realtime = inject(RealtimeService);
  private destroyRef = inject(DestroyRef);
  auth = inject(AuthService);

  readonly ProposalStatus = ProposalStatus;
  readonly UserRole = UserRole;
  readonly ProposalClassification = ProposalClassification;
  readonly ClassificationLabels = ClassificationLabels;
  readonly NotApplicableReason = NotApplicableReason;
  readonly NotApplicableReasonLabels = NotApplicableReasonLabels;
  readonly classificationOptions = Object.values(ProposalClassification);
  readonly reasonOptions = Object.values(NotApplicableReason);

  proposal = signal<Proposal | null>(null);
  users = signal<User[]>([]);
  measurementFields = signal<FormField[]>([]);
  blindPolicy = signal<BlindReviewPolicy | null>(null);
  loading = signal(true);
  busy = signal(false);
  /** تحديث وصل من مستخدم آخر أثناء فتح الصفحة. */
  liveUpdated = signal(false);
  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);

  // نماذج الإجراءات
  screenNotes = '';
  classification: ProposalClassification | null = null;
  notApplicableReason: NotApplicableReason | null = null;
  study = '';
  recommendation = '';
  execNotes = '';
  ownerId: number | null = null;
  escalationNote = '';

  ngOnInit() {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.load(id);
    this.proposalService.blindReviewPolicy().subscribe({ next: p => this.blindPolicy.set(p), error: () => {} });
    this.formSettings.getAll(true).subscribe({
      next: f => this.measurementFields.set(f.filter(x => x.section === 'ImpactMeasurement')),
      error: () => {},
    });
    if (this.auth.role() === UserRole.Admin) {
      this.userService.getAll().subscribe(u => this.users.set(u));
    }

    // تحديث لحظي: إن نقل مستخدم آخر هذا المقترح لمرحلة جديدة نعيد تحميله فوراً
    this.realtime.changesFor(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (this.busy()) return; // تغييرنا نحن — handle() يعيد التحميل بنفسه
        this.liveUpdated.set(true);
        this.load(id, true);
      });
  }

  load(id: number, silent = false) {
    if (!silent) this.loading.set(true);
    this.proposalService.getById(id).subscribe({
      next: p => {
        this.proposal.set(p);
        this.classification = p.classification;
        this.notApplicableReason = p.notApplicableReasonValue;
        this.study = p.committeeStudy ?? '';
        this.recommendation = p.committeeRecommendation ?? '';
        this.ownerId = p.ownerId;
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  goBack() { this.location.back(); }

  activeImpacts(p: Proposal): string[] { return selectedImpacts(p); }

  hoursLeft(p: Proposal): number {
    return Math.round((new Date(p.slaDueAt).getTime() - Date.now()) / 3_600_000);
  }

  isOverdue(p: Proposal): boolean {
    return this.hoursLeft(p) < 0
      && p.status !== ProposalStatus.Accepted && p.status !== ProposalStatus.Rejected
      && p.status !== ProposalStatus.ReturnedForEdit;
  }

  // ---- صلاحيات الإجراءات: من الخادم ----
  get role() { return this.auth.role(); }
  get isMine() { return this.proposal()?.submitterId === this.auth.currentUser()?.id; }

  canScreen(): boolean { return !!this.proposal()?.actions.canScreen; }
  canStudy(): boolean { return !!this.proposal()?.actions.canStudy; }
  canSign(): boolean { return !!this.proposal()?.actions.canSign; }
  canDecide(): boolean { return !!this.proposal()?.actions.canDecide; }
  canAssignOwner(): boolean { return !!this.proposal()?.actions.canAssignOwner; }
  canEscalate(): boolean { return !!this.proposal()?.actions.canEscalate; }
  canEdit(): boolean { return !!this.proposal()?.actions.canEdit; }

  /** أُعيد توجيه المقترح إليّ تلقائياً بعد تأخر الفرز. */
  get reroutedToMe(): boolean {
    const p = this.proposal();
    return !!p && p.escalatedToId === this.auth.currentUser()?.id && p.actions.canScreen;
  }

  get myVote() {
    const uid = this.auth.currentUser()?.id;
    return this.proposal()?.committeeVotes.find(v => v.memberId === uid) ?? null;
  }

  private handle(obs: Observable<Proposal>, successMsg: string) {
    this.busy.set(true);
    this.liveUpdated.set(false);
    this.errorMessage.set(null);
    this.successMessage.set(null);
    obs.subscribe({
      next: updated => {
        this.successMessage.set(successMsg);
        this.load(updated?.id ?? this.proposal()!.id, true);
        // نترك نافذة قصيرة لتجاهل صدى الدفع اللحظي لتغييرنا نحن
        setTimeout(() => this.busy.set(false), 800);
      },
      error: (err: { error?: { message?: string } }) => {
        this.busy.set(false);
        this.errorMessage.set(err?.error?.message ?? 'تعذّر تنفيذ الإجراء.');
      },
    });
  }

  screen(action: 'Approve' | 'ReturnForEdit' | 'Reject') {
    const p = this.proposal(); if (!p) return;
    if (action !== 'Approve' && !this.screenNotes.trim()) {
      this.errorMessage.set(action === 'Reject' ? 'الرجاء ذكر سبب الرفض.' : 'الرجاء إضافة ملاحظات للموظف.');
      return;
    }
    const msg = action === 'Approve' ? 'تمت الإحالة للجنة.'
      : action === 'ReturnForEdit' ? 'أُعيد المقترح للموظف للتعديل.' : 'تم رفض المقترح.';
    this.handle(this.proposalService.screen(p.id, { action, notes: this.screenNotes || undefined }), msg);
  }

  saveStudy() {
    const p = this.proposal(); if (!p) return;
    if (!this.classification) { this.errorMessage.set('الرجاء تحديد تصنيف المقترح.'); return; }
    if (this.classification === ProposalClassification.NotApplicable && !this.notApplicableReason) {
      this.errorMessage.set('الرجاء تحديد سبب عدم قابلية التطبيق.'); return;
    }
    if (!this.study.trim() || !this.recommendation.trim()) {
      this.errorMessage.set('الرجاء تعبئة الدراسة والتوصية.'); return;
    }
    this.handle(this.proposalService.saveCommitteeStudy(p.id, {
      classification: this.classification,
      notApplicableReasonValue: this.classification === ProposalClassification.NotApplicable ? this.notApplicableReason : null,
      committeeStudy: this.study,
      committeeRecommendation: this.recommendation,
    }), 'تم حفظ الدراسة والتوصية.');
  }

  sign() {
    const p = this.proposal(); const uid = this.auth.currentUser()?.id;
    if (!p || !uid) return;
    this.handle(this.proposalService.committeeSign(p.id, uid), 'تم تسجيل توقيعك.');
  }

  decide(decision: 'Accepted' | 'Rejected') {
    const p = this.proposal(); if (!p) return;
    if (decision === 'Rejected' && !this.execNotes.trim()) {
      this.errorMessage.set('الرجاء ذكر سبب عدم الموافقة.'); return;
    }
    this.handle(this.proposalService.executiveDecision(p.id, { decision, notes: this.execNotes || undefined }),
      decision === 'Accepted' ? 'تمت الموافقة على المقترح وجُدول قياس أثره الفعلي.' : 'تم رفض المقترح.');
  }

  assignOwner() {
    const p = this.proposal(); if (!p || !this.ownerId) return;
    this.handle(this.proposalService.assignOwner(p.id, { ownerId: this.ownerId }), 'تم تعيين مسؤول التنفيذ.');
  }

  escalate() {
    const p = this.proposal(); if (!p) return;
    if (!this.escalationNote.trim()) { this.errorMessage.set('الرجاء كتابة سبب التصعيد.'); return; }
    this.handle(this.proposalService.escalate(p.id, this.escalationNote), 'تم تصعيد المقترح لمدير النظام.');
  }

  /** حفظ/اعتماد قياس الأثر من اللوحة الفرعية. */
  onImpactUpdated(updated: Proposal) {
    this.proposal.set(updated);
  }

  openForm() { const p = this.proposal(); if (p) this.proposalService.openOfficialForm(p.id); }
  openMinutes() { const p = this.proposal(); if (p) this.proposalService.openMinutes(p.id); }
  openCertificate() { const p = this.proposal(); if (p) this.proposalService.openCertificate(p.id); }
}
