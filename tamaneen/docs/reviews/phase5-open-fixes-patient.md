Read /tmp/claude-0/-home-user-ibrahim/7db83462-ec14-55f6-bda4-8fd12e4a7ce7/scratchpad/review/PHASE5_BRIEF.md. Its conventions, verification and finish rules apply, except that INotifier is now implemented, so use it normally.

Your worktree starts from claude/brave-bardeen-3nott4, which has all of phase 5 merged.

TASK: fix every BLOCKER and MAJOR in the phase 5 PATIENT review:
/tmp/claude-0/-home-user-ibrahim/7db83462-ec14-55f6-bda4-8fd12e4a7ce7/scratchpad/review/r5p.md
Fix a minor only when it is trivial and in a file you are already changing.

1. **P41/P54 lobby:**
   - when the session is cancelled by the therapist, send the patient to P67;
   - when it is missed, send them to P63;
   - never link a session that didn't happen to P43.
   **P43:** only show the "you finished your session" state for a session that actually took place. Otherwise redirect to the right screen.
2. **P63 «مو بخير»:** it must be sent to the server.
   - It passes the crisis gate where there is text.
   - It alerts the care team: the patient's therapist and the supervisors or on-call, as the guide says (grep guide.md for P63 and EC-53).
   - Show the crisis resources.
   - The same applies to the other answers: store them.
3. **P53 «أحتاج أرجع للجلسات»:** run the short safety check the design requires before re-entry. Reuse the existing safety-check (P23) mechanics and gate.
4. **P38:** add the missing «ما اتفقتما عليه» items: the follow-up call time and the support person.
   - Add structured fields on the crisis follow-up, set when the responder closes the event (worker close or follow-up data).
   - Keep the patient-facing text neutral.
5. **P46/P29:** add the homework empty state's «تمرين تنفس الآن» button, per the design.
6. **P67:** rebooking with the same therapist after a therapist cancellation is a priority reschedule of the cancelled appointment, per the guide:
   - it is not a new booking;
   - it keeps the sequence number;
   - it has no 24-hour limit.
7. **Fix the minors on the S16 → P29/P10 flow:**
   - a cancelled exercise shows its real end date;
   - the weekly dots count this week only;
   - `homework.cancelled` gets proper ar/en text;
   - the start date uses the patient's timezone.
   Also fix the S35 section length mismatch: use one limit on both sides.

DEFERRED, do NOT build: the P67 same-time substitute therapist. It needs S65, planned for phase 8. Make sure the UI doesn't show a broken placeholder.

Do not touch the staff-side items. Another agent is fixing r5s.md:
- S18, S15, S20, S21, S32, S33, S35 dialog, S41, S57;
- NotificationCatalog entries for EC-52, EC-53 and S41 links.
If you need a new notification kind for P63, add its catalog entry in a separate, clearly marked block.
