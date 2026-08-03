import type { InputHTMLAttributes } from "react";

export function TextField(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className="input" {...props} />;
}
