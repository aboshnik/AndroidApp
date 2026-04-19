import { apiClient } from './client'
import type {
  AuthRegisterRequest,
  AuthRegisterResponse,
  AvatarUploadResponse,
  ConfirmDeviceLoginRequest,
  EmployeeLoginRequest,
  EmployeeLoginResponse,
  EmployeeProfileResponse,
  EmployeeWorkScheduleResponse,
} from './types'

export async function login(request: EmployeeLoginRequest) {
  const { data } = await apiClient.post<EmployeeLoginResponse>('api/employee/login', request)
  return data
}

export async function confirmDeviceLogin(request: ConfirmDeviceLoginRequest) {
  const { data } = await apiClient.post<EmployeeLoginResponse>('api/employee/confirm-device-login', request)
  return data
}

export async function registerByEmployee(request: AuthRegisterRequest) {
  const { data } = await apiClient.post<AuthRegisterResponse>('api/auth/register', request)
  return data
}

export async function getProfile(params: { employeeId?: string; login?: string }) {
  const { data } = await apiClient.get<EmployeeProfileResponse>('api/employee/profile', { params })
  return data
}

export async function uploadAvatar(login: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  const { data } = await apiClient.post<AvatarUploadResponse>('api/employee/avatar', form, {
    params: { login },
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

export async function getWorkSchedule(params: { employeeId?: string; login?: string }) {
  const { data } = await apiClient.get<EmployeeWorkScheduleResponse>('api/employee/work-schedule', { params })
  return data
}
