<script setup lang="ts">
import { ArrowRight, Key, Lock, UserFilled } from '@element-plus/icons-vue'
import { useRoute, useRouter } from 'vue-router'
import { ref } from 'vue'
import AppTopbar from '@/components/AppTopbar.vue'
import { authSession } from '@/services/authSession'

const route = useRoute()
const router = useRouter()
const isPreviewMode = import.meta.env.DEV
const loginIllustrationUrl = '/images/whaledeck-login-illustration.png'
const authenticating = ref(false)

async function authenticate() {
  if (authenticating.value) return
  authenticating.value = true
  const requestedReturnUrl = typeof route.query.returnUrl === 'string' ? route.query.returnUrl : '/'
  const returnUrl =
    requestedReturnUrl.startsWith('/') && !requestedReturnUrl.startsWith('//')
      ? requestedReturnUrl
      : '/'

  if (authSession.authenticatePreview()) {
    await router.replace(returnUrl)
    return
  }

  authSession.redirectToAuthentik(returnUrl)
}
</script>

<template>
  <div class="login-page">
    <div class="login-page__mesh" aria-hidden="true" />
    <AppTopbar login-mode />

    <main class="login-layout">
      <section class="login-visual">
        <div class="login-visual__copy login-motion login-motion--one">
          <el-tag type="primary" effect="light" round>WHALEDECK PLATFORM</el-tag>
          <h1>一套身份，进入整个工作站协同平台。</h1>
          <p>
            凭据、用户资料、用户组和多因素认证均由 Authentik 统一管理，WhaleDeck 不接触你的密码。
          </p>
        </div>

        <div class="login-illustration login-motion login-motion--two" aria-hidden="true">
          <span class="login-illustration__halo" />
          <img :src="loginIllustrationUrl" alt="" />
        </div>

        <div class="login-trust login-motion login-motion--three">
          <span
            ><el-icon><Lock /></el-icon> HttpOnly 会话</span
          >
          <span
            ><el-icon><Key /></el-icon> MFA 与 Passkey</span
          >
          <span
            ><el-icon><UserFilled /></el-icon> 统一用户资料</span
          >
        </div>
      </section>

      <section class="login-panel login-motion login-motion--two">
        <div class="login-card">
          <div class="login-card__icon">
            <el-icon><UserFilled /></el-icon>
          </div>
          <span class="login-card__eyebrow">WELCOME BACK</span>
          <h2>使用工作站账号登录</h2>
          <p>用户名、密码及多因素认证均由 Authentik 直接核验。</p>
          <el-alert
            v-if="route.query.reason === 'auth-expired'"
            title="登录验证已失效，临时登录信息已清理，请重新登录。"
            type="warning"
            :closable="false"
            show-icon
          />

          <div class="identity-provider">
            <span class="identity-provider__mark"
              ><el-icon><Lock /></el-icon
            ></span>
            <span>
              <strong>Authentik</strong>
              <small>凭据验证与用户资料来源</small>
            </span>
            <el-tag type="success" size="small" effect="light" round>受信任</el-tag>
          </div>

          <el-button
            class="authentik-login"
            type="primary"
            size="large"
            :loading="authenticating"
            @click="authenticate"
          >
            使用用户名和密码登录
            <el-icon><ArrowRight /></el-icon>
          </el-button>

          <div class="login-card__notice">
            <el-icon><Lock /></el-icon>
            <span v-if="isPreviewMode">前端预览模式：点击后模拟 Authentik 验证成功。</span>
            <span v-else>WhaleDeck 不保存密码，验证过程由 Authentik 完成。</span>
          </div>
        </div>
        <p class="login-panel__footer">可通过工作站当前可用的局域网地址访问</p>
      </section>
    </main>
  </div>
</template>
