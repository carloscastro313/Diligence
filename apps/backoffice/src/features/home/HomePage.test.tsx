import { render, screen } from '@testing-library/react';
import { HomePage } from './HomePage';

test('muestra el título de Backoffice', () => {
  render(<HomePage />);
  expect(screen.getByRole('heading', { name: 'Diligencia - Backoffice' })).toBeInTheDocument();
});
