import { QueryClient } from '@tanstack/react-query';

// staleTime corto: el beforeLoad de /caso precarga y la página reutiliza sin pedir dos veces.
export const queryClient = new QueryClient({ defaultOptions: { queries: { staleTime: 5_000 } } });
