Read /tmp/claude-0/-home-user-ibrahim/7db83462-ec14-55f6-bda4-8fd12e4a7ce7/scratchpad/review/PHASE5_BRIEF.md. Its conventions, verification and finish rules apply, except that INotifier is now implemented, so use it normally.

Your worktree starts from claude/brave-bardeen-3nott4, which has all of phase 5 merged.

TASK: fix the BLOCKER and every MAJOR in the phase 5 STAFF review:
/tmp/claude-0/-home-user-ibrahim/7db83462-ec14-55f6-bda4-8fd12e4a7ce7/scratchpad/review/r5s.md
Fix a minor only when it is trivial and in a file you are already changing.

1. **S18:** «تمت الجلسة» opens S33 session notes for that appointment. Also give each past session a notes link in the schedule. Add «إعادة جدولة» for the therapist, with a staff endpoint:
   - pick a new free time of the therapist's own;
   - notify the patient;
   - audited.
2. **S15:** keep the plan draft locally while editing and resend on reconnect, per the design. Never keep clinical text in plain browser storage: keep it in memory, or encrypt it the way the patient app does. Choose and justify.
3. **S20:** the 30-day history includes risk raises and no-shows, not only closed crisis events, using the existing data under RLS.
4. **S21:** the licence card shows expiry date, who verified it and when, and a 30-day warning.
   - Add the licence fields: expiry, verifiedBy, verifiedAt.
   - Seed dev values.
   - Only an admin or the lead may change verification; S05 comes later, so provide only what's needed.
5. **S32:**
   - Enforce the 15-minute idle timeout on the staff portal: sign out, keeping the "return to page" behaviour. The server side should also limit staff refresh accordingly, if the guide says so.
   - The time zone can be chosen.
   - Ending other sessions asks for confirmation.
6. **S33:**
   - Show «ما أراد المريض الحديث عنه», the patient's pre-session note (the API exists: SessionPrep).
   - Add «حجز الجلسة القادمة».
7. **S35 «إضافة شخص»:** add a relationship field. The phone is optional, per the design.
8. **S41:** the new-booking notification links to the clinical file waiting for approval (/patients/:id/file) when the file is pending.
   **NotificationCatalog:** add proper important supervisor alerts for EC-52 (rebook needed) and EC-53 (repeated or high-risk no-show). They must not arrive as a generic «لديك إشعار جديد.».
9. **S57:**
   - Block closing when the patient asked to continue care. Store that request: P53 has a way back, so link to it.
   - The banner says which reason blocks the closure.

Do not touch the patient-side items; another agent is fixing r5p.md. Coordinate only through additive changes in shared files.
