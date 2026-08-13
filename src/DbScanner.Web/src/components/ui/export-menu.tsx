"use client";

import { useEffect, useRef } from "react";

export type ExportOption = {
  label: string;
  description: string;
  onSelect: () => void;
};

type ExportMenuProps = {
  options: ExportOption[];
  label?: string;
};

export function ExportMenu({ options, label = "Dışa Aktar" }: ExportMenuProps) {
  const detailsRef = useRef<HTMLDetailsElement>(null);

  useEffect(() => {
    function closeOnOutsideClick(event: PointerEvent) {
      if (!detailsRef.current?.contains(event.target as Node)) {
        detailsRef.current?.removeAttribute("open");
      }
    }

    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        detailsRef.current?.removeAttribute("open");
      }
    }

    document.addEventListener("pointerdown", closeOnOutsideClick);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsideClick);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, []);

  return (
    <details className="export-menu" ref={detailsRef}>
      <summary className="button button--secondary button--compact">{label}</summary>
      <div className="export-menu__popover">
        {options.map((option) => (
          <button
            key={option.label}
            className="export-menu__option"
            type="button"
            onClick={() => {
              detailsRef.current?.removeAttribute("open");
              option.onSelect();
            }}
          >
            <strong>{option.label}</strong>
            <span>{option.description}</span>
          </button>
        ))}
      </div>
    </details>
  );
}
