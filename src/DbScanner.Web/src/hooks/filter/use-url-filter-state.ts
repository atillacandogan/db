export type UrlFilterState = {
  page: number;
  pageSize: number;
  search?: string;
};

export function useUrlFilterState(): UrlFilterState {
  return {
    page: 1,
    pageSize: 20,
  };
}
