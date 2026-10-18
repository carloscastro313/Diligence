import { render, screen } from '@testing-library/react';
import { HomePage } from './HomePage';

test('muestra el título de Portal', () => {
  render(<HomePage />);
  expect(screen.getByRole('heading', { name: 'Diligencia - Portal' })).toBeInTheDocument();
});
