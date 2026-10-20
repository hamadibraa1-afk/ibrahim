/** Digits keep their order inside Arabic text. */
export function PhoneNumber({ number }: { number: string }) {
  return (
    <bdi dir="ltr" className="tabular-nums">
      {number}
    </bdi>
  );
}
