import { apiRequest, apiUrl } from '@/services/apiClient'

export async function navigateToLogout() {
  const csrf = await apiRequest<{ formFieldName: string; token: string }>('auth/csrf')
  if (!csrf.formFieldName || !csrf.token) throw new Error('退出验证信息不可用，请重试')
  const form = document.createElement('form')
  form.method = 'POST'
  form.action = apiUrl('auth/logout')
  form.hidden = true
  const field = document.createElement('input')
  field.type = 'hidden'
  field.name = csrf.formFieldName
  field.value = csrf.token
  form.append(field)
  document.body.append(form)
  form.submit()
}
