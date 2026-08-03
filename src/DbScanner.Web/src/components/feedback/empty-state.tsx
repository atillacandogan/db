type EmptyStateProps = {
  title: string;
  description?: string;
};

export function EmptyState({ title, description }: EmptyStateProps) {
  return (
    <div className="placeholder">
      <strong>{title}</strong>
      {description ? <p className="muted">{description}</p> : null}
    </div>
  );
}
