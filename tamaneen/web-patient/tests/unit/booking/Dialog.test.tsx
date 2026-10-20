import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, useState } from 'react';
import { Dialog } from '@/components/booking/Dialog';

function Harness() {
  const [open, setOpen] = useState(false);
  const keep = useRef<HTMLButtonElement>(null);
  return (
    <>
      <button onClick={() => setOpen(true)}>open</button>
      <Dialog open={open} onClose={() => setOpen(false)} title="Title" description="Body" initialFocus={keep}>
        <button>danger</button>
        <button ref={keep} onClick={() => setOpen(false)}>
          keep
        </button>
      </Dialog>
    </>
  );
}

describe('Dialog', () => {
  it('is a labelled modal that traps focus, closes on Escape and restores focus', async () => {
    const user = userEvent.setup();
    render(<Harness />);
    const opener = screen.getByRole('button', { name: 'open' });
    await user.click(opener);

    const dialog = screen.getByRole('dialog', { name: 'Title' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription('Body');
    expect(screen.getByRole('button', { name: 'keep' })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole('button', { name: 'danger' })).toHaveFocus();
    await user.tab({ shift: true });
    expect(screen.getByRole('button', { name: 'keep' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(opener).toHaveFocus();
  });
});
