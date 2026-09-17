import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import AuthPanel from './AuthPanel';
const api=vi.hoisted(()=>({registerPatient:vi.fn(),login:vi.fn()}));
vi.mock('../lib/api',()=>api);
beforeEach(()=>vi.resetAllMocks()); afterEach(cleanup);
function fill(password='StrongPassword123!',confirmation=password) {
  fireEvent.click(screen.getByRole('button',{name:'Create patient account'}));
  for(const [label,value] of [['First Name','Synthetic'],['Last Name','Patient'],['Email','synthetic@test.example'],['Password',password],['Confirm Password',confirmation]])
    fireEvent.change(screen.getByLabelText(label,{exact:true}),{target:{value}});
  fireEvent.click(screen.getByRole('button',{name:'Register'}));
}
it('registers a patient without a role selector and returns to login',async()=>{
  api.registerPatient.mockResolvedValue(undefined);render(<AuthPanel user={null} onLogin={vi.fn()} />);fill();
  expect(await screen.findByText('Your patient account is ready. Sign in to continue.')).toBeTruthy();
  expect(api.registerPatient).toHaveBeenCalledWith({firstName:'Synthetic',lastName:'Patient',email:'synthetic@test.example',password:'StrongPassword123!'});
  expect(screen.getByLabelText('Password')).toHaveValue('');expect(screen.queryByRole('combobox')).toBeNull();
});
it('rejects password mismatch without submitting',()=>{
  render(<AuthPanel user={null} onLogin={vi.fn()} />);fill('StrongPassword123!','DifferentPassword123!');
  expect(screen.getByRole('alert')).toHaveTextContent('Passwords do not match');expect(api.registerPatient).not.toHaveBeenCalled();
});
it('rejects weak passwords and permits return to sign in',()=>{
  render(<AuthPanel user={null} onLogin={vi.fn()} />);fill('weak');
  expect(screen.getByRole('alert')).toHaveTextContent('12–128');expect(api.registerPatient).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button',{name:'Already have an account? Sign in'}));expect(screen.getByRole('button',{name:'Log in'})).toBeTruthy();
});
