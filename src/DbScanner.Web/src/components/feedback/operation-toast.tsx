"use client";

import { useEffect, useRef } from "react";

type OperationToastProps = {
  message: string;
  variant: "success" | "error";
  onClose: () => void;
};

export function OperationToast({ message, variant, onClose }: OperationToastProps) {
  const onCloseRef = useRef(onClose);

  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => onCloseRef.current(), 5000);
    return () => window.clearTimeout(timeoutId);
  }, [message, variant]);

  return (
    <div
      className={`operation-toast operation-toast--${variant}`}
      role={variant === "error" ? "alert" : "status"}
      aria-live={variant === "error" ? "assertive" : "polite"}
    >
      <span>{message}</span>
      <button
        className="operation-toast__close"
        type="button"
        aria-label="Bildirimi kapat"
        onClick={onClose}
      >
        x
      </button>
    </div>
  );
}
