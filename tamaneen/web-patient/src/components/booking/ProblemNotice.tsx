import type { ReactNode } from 'react';
import { Notice } from '@/components/ui/Notice';
import type { ShownProblem } from './problem';

/** An API problem (already localized) as an inline alert: title in bold, detail below, optional action. */
export function ProblemNotice({ problem, tone = 'danger', action, className = '' }: { problem: ShownProblem; tone?: 'danger' | 'warning'; action?: ReactNode; className?: string }) {
  return (
    <Notice tone={tone} className={className}>
      <p className="font-semibold">{problem.title}</p>
      {problem.detail ? <p className="mt-1">{problem.detail}</p> : null}
      {action ? <div className="mt-2">{action}</div> : null}
    </Notice>
  );
}
