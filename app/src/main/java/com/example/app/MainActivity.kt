package com.example.app

import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Bundle
import android.os.Build
import android.util.Log
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import androidx.core.app.NotificationManagerCompat
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.text.InputType
import android.text.method.PasswordTransformationMethod
import androidx.appcompat.widget.AppCompatImageButton
import android.widget.CheckBox
import android.widget.Toast
import com.example.app.api.ApiClient
import com.example.app.api.ConfirmDeviceLoginRequest
import com.example.app.api.EmployeeLoginRequest
import com.example.app.api.EmployeeLoginResult
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.util.UUID

class MainActivity : BaseActivity() {
    companion object {
        const val EXTRA_PREFILL_LOGIN = "extra_prefill_login"
        const val EXTRA_PREFILL_PASSWORD = "extra_prefill_password"
        const val EXTRA_PREFILL_REMEMBER_ME = "extra_prefill_remember_me"
        const val EXTRA_AUTO_LOGIN_SECONDS = "extra_auto_login_seconds"
    }

    private val logTag = "MainActivityNet"

    /** ID попытки входа с нового устройства (код на другое устройство). */
    private var pendingDeviceLoginAttemptId: Int? = null

    private val requestNotifPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (!granted) {
            promptEnableNotificationsInSettings()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Auto-login if session is still alive (5 minutes)
        if (SessionManager.isActive(this)) {
            SessionManager.startHomeWithSavedSession(this)
            return
        }

        setContentView(R.layout.activity_main)

        ensureNotificationsPermissionOnStart()

        val etLogin = findViewById<EditText>(R.id.etLogin)
        val etPassword = findViewById<EditText>(R.id.etPassword)
        val cbRememberMe = findViewById<CheckBox>(R.id.cbRememberMe)
        val btnTogglePassword = findViewById<AppCompatImageButton>(R.id.btnTogglePassword)
        val btnLogin = findViewById<Button>(R.id.btnLogin)
        val panelDeviceCode = findViewById<View>(R.id.panelDeviceCode)
        val etDeviceCode = findViewById<EditText>(R.id.etDeviceCode)
        val btnConfirmDeviceCode = findViewById<Button>(R.id.btnConfirmDeviceCode)
        val btnCancelDeviceCode = findViewById<Button>(R.id.btnCancelDeviceCode)
        cbRememberMe.isChecked = getSharedPreferences("auth", MODE_PRIVATE).getBoolean("rememberMe", false)
        if (cbRememberMe.isChecked) {
            val lastLogin = SessionManager.getLastLogin(this)
            if (lastLogin.isNotBlank()) etLogin.setText(lastLogin)
        }

        cbRememberMe.setOnCheckedChangeListener { _, checked ->
            if (checked) {
                val lastLogin = SessionManager.getLastLogin(this)
                if (etLogin.text?.toString()?.trim().isNullOrEmpty() && lastLogin.isNotBlank()) {
                    etLogin.setText(lastLogin)
                }
            }
        }

        var isPasswordVisible = false
        fun applyPasswordVisibility() {
            etPassword.transformationMethod = if (isPasswordVisible) null else PasswordTransformationMethod.getInstance()
            etPassword.inputType = if (isPasswordVisible) {
                InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
            } else {
                InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            }
            etPassword.setSelection(etPassword.text?.length ?: 0)
        }

        btnTogglePassword.setOnClickListener {
            isPasswordVisible = !isPasswordVisible
            btnTogglePassword.setImageResource(
                if (isPasswordVisible) R.drawable.ic_visibility else R.drawable.ic_visibility_off
            )
            applyPasswordVisibility()
        }

        btnLogin.setOnClickListener {
            val login = etLogin.text.toString().trim()
            val password = etPassword.text.toString().trim()

            etLogin.error = null
            etPassword.error = null

            when {
                login.isEmpty() -> {
                    etLogin.error = getString(R.string.error_field_required)
                    etLogin.requestFocus()
                }
                password.isEmpty() -> {
                    etPassword.error = getString(R.string.error_field_required)
                    etPassword.requestFocus()
                }
                else -> {
                    pendingDeviceLoginAttemptId = null
                    panelDeviceCode.visibility = View.GONE
                    etDeviceCode.text?.clear()
                    btnLogin.isEnabled = false
                    scope.launch {
                        try {
                            val response = withContext(Dispatchers.IO) {
                                val deviceId = getSharedPreferences("auth", MODE_PRIVATE)
                                    .getString("device_id", null)
                                    ?: UUID.randomUUID().toString().also { newId ->
                                        getSharedPreferences("auth", MODE_PRIVATE).edit().putString("device_id", newId).apply()
                                    }
                                val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
                                ApiClient.employeeApi.login(
                                    EmployeeLoginRequest(
                                        login = login,
                                        password = password,
                                        deviceId = deviceId,
                                        deviceName = deviceName
                                    )
                                )
                            }

                            val body = response.body()
                            if (!response.isSuccessful || body == null) {
                                Toast.makeText(
                                    this@MainActivity,
                                    getString(R.string.error_network),
                                    Toast.LENGTH_LONG
                                ).show()
                                return@launch
                            }

                            if (body.requiresDeviceCode && body.pendingAttemptId != null && body.pendingAttemptId > 0) {
                                pendingDeviceLoginAttemptId = body.pendingAttemptId
                                panelDeviceCode.visibility = View.VISIBLE
                                etDeviceCode.text?.clear()
                                etDeviceCode.requestFocus()
                                Toast.makeText(
                                    this@MainActivity,
                                    body.message,
                                    Toast.LENGTH_LONG
                                ).show()
                                return@launch
                            }

                            if (!body.success || body.result == null) {
                                Toast.makeText(
                                    this@MainActivity,
                                    body.message,
                                    Toast.LENGTH_LONG
                                ).show()
                                return@launch
                            }

                            onLoginSuccess(login, cbRememberMe.isChecked, body.result)
                        } catch (e: Exception) {
                            val details = buildErrorDetails(e)
                            Log.e(logTag, details, e)
                            Toast.makeText(
                                this@MainActivity,
                                "${getString(R.string.error_network)} $details",
                                Toast.LENGTH_LONG
                            ).show()
                            showNetworkErrorDialog(details)
                        } finally {
                            btnLogin.isEnabled = true
                        }
                    }
                }
            }
        }

        btnConfirmDeviceCode.setOnClickListener {
            val login = etLogin.text.toString().trim()
            val password = etPassword.text.toString().trim()
            val code = etDeviceCode.text?.toString()?.trim().orEmpty()
            val attemptId = pendingDeviceLoginAttemptId
            if (login.isEmpty() || password.isEmpty()) {
                Toast.makeText(this, getString(R.string.error_field_required), Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            if (attemptId == null) {
                Toast.makeText(this, "Сначала нажмите «Войти»", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            if (code.length != 6) {
                Toast.makeText(this, "Введите 6 цифр кода", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }
            btnConfirmDeviceCode.isEnabled = false
            btnLogin.isEnabled = false
            scope.launch {
                try {
                    val response = withContext(Dispatchers.IO) {
                        val deviceId = getSharedPreferences("auth", MODE_PRIVATE)
                            .getString("device_id", null)
                            ?: UUID.randomUUID().toString().also { newId ->
                                getSharedPreferences("auth", MODE_PRIVATE).edit().putString("device_id", newId).apply()
                            }
                        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
                        ApiClient.employeeApi.confirmDeviceLogin(
                            ConfirmDeviceLoginRequest(
                                login = login,
                                password = password,
                                deviceId = deviceId,
                                deviceName = deviceName,
                                attemptId = attemptId,
                                code = code
                            )
                        )
                    }
                    val body = response.body()
                    if (!response.isSuccessful || body == null) {
                        Toast.makeText(
                            this@MainActivity,
                            getString(R.string.error_network),
                            Toast.LENGTH_LONG
                        ).show()
                        return@launch
                    }
                    if (!body.success || body.result == null) {
                        Toast.makeText(
                            this@MainActivity,
                            body.message,
                            Toast.LENGTH_LONG
                        ).show()
                        return@launch
                    }
                    pendingDeviceLoginAttemptId = null
                    panelDeviceCode.visibility = View.GONE
                    etDeviceCode.text?.clear()
                    onLoginSuccess(login, cbRememberMe.isChecked, body.result)
                } catch (e: Exception) {
                    val details = buildErrorDetails(e)
                    Log.e(logTag, details, e)
                    Toast.makeText(
                        this@MainActivity,
                        "${getString(R.string.error_network)} $details",
                        Toast.LENGTH_LONG
                    ).show()
                } finally {
                    btnConfirmDeviceCode.isEnabled = true
                    btnLogin.isEnabled = true
                }
            }
        }

        btnCancelDeviceCode.setOnClickListener {
            pendingDeviceLoginAttemptId = null
            panelDeviceCode.visibility = View.GONE
            etDeviceCode.text?.clear()
            Toast.makeText(
                this,
                "Для получения нового кода снова нажмите «Войти»",
                Toast.LENGTH_LONG
            ).show()
        }

        // Регистрация: табельный номер + телефон (пароль придёт в StekloSecurity)
        findViewById<Button>(R.id.btnRegister).setOnClickListener {
            startActivity(Intent(this, RegisterActivity::class.java))
        }

        applyReloginPrefill(etLogin, etPassword, cbRememberMe, btnLogin)
    }

    private fun onLoginSuccess(login: String, rememberMe: Boolean, r: EmployeeLoginResult) {
        SessionManager.saveFromLogin(
            context = this,
            login = login,
            employeeId = r.employeeId,
            lastName = r.lastName,
            firstName = r.firstName,
            phone = r.phone,
            canCreatePosts = r.canCreatePosts || r.isTechAdmin,
            isTechAdmin = r.isTechAdmin,
            canUseDevConsole = r.canUseDevConsole,
            rememberMe = rememberMe
        )
        runCatching {
            FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
                val deviceId = getSharedPreferences("auth", MODE_PRIVATE)
                    .getString("device_id", "")?.trim().orEmpty()
                val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()
                scope.launch(Dispatchers.IO) {
                    runCatching {
                        ApiClient.pushTokenApi.register(
                            com.example.app.api.RegisterPushTokenRequest(
                                login = login,
                                token = token,
                                deviceId = deviceId,
                                deviceName = deviceName
                            )
                        )
                    }
                }
            }
        }
        ensureNotificationsPermissionOnStart()
        SessionManager.startHomeWithSavedSession(this)
    }

    private fun ensureNotificationsPermissionOnStart() {
        // Android 13+ runtime permission
        if (Build.VERSION.SDK_INT >= 33) {
            val granted = ContextCompat.checkSelfPermission(
                this,
                android.Manifest.permission.POST_NOTIFICATIONS
            ) == PackageManager.PERMISSION_GRANTED
            if (!granted) {
                requestNotifPermission.launch(android.Manifest.permission.POST_NOTIFICATIONS)
                return
            }
        }

        // All versions: user may have disabled notifications in system settings
        if (!NotificationManagerCompat.from(this).areNotificationsEnabled()) {
            promptEnableNotificationsInSettings()
        }
    }

    private fun promptEnableNotificationsInSettings() {
        // Keep it simple and reliable (no extra deps): open app notification settings.
        val i = Intent().apply {
            action = "android.settings.APP_NOTIFICATION_SETTINGS"
            putExtra("android.provider.extra.APP_PACKAGE", packageName)
            data = Uri.fromParts("package", packageName, null)
        }
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle(getString(R.string.notif_perm_title))
                .setMessage(getString(R.string.notif_perm_text))
                .setPositiveButton(getString(R.string.notif_perm_open_settings)) { _, _ ->
                    runCatching { startActivity(i) }
                }
                .setNegativeButton(getString(R.string.notif_perm_later), null)
        )
    }

    private fun buildErrorDetails(e: Throwable): String {
        var t: Throwable? = e
        val chain = mutableListOf<String>()
        var guard = 0
        while (t != null && guard < 5) {
            val name = t.javaClass.simpleName
            val msg = t.message?.trim().orEmpty()
            chain += if (msg.isNotBlank()) "$name: $msg" else name
            t = t.cause
            guard++
        }
        return chain.joinToString(" -> ")
    }

    private fun showNetworkErrorDialog(details: String) {
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setTitle("Подробности ошибки сети")
                .setMessage(details)
                .setPositiveButton("OK", null)
        )
    }

    private fun applyReloginPrefill(
        etLogin: EditText,
        etPassword: EditText,
        cbRememberMe: CheckBox,
        btnLogin: Button
    ) {
        val login = intent.getStringExtra(EXTRA_PREFILL_LOGIN)?.trim().orEmpty()
        val password = intent.getStringExtra(EXTRA_PREFILL_PASSWORD)?.trim().orEmpty()
        if (login.isBlank() || password.isBlank()) return
        etLogin.setText(login)
        etPassword.setText(password)
        cbRememberMe.isChecked = intent.getBooleanExtra(EXTRA_PREFILL_REMEMBER_ME, true)
        val autoSeconds = intent.getIntExtra(EXTRA_AUTO_LOGIN_SECONDS, 0).coerceAtLeast(0)
        if (autoSeconds > 0) {
            Toast.makeText(this, "Автовход через $autoSeconds сек.", Toast.LENGTH_SHORT).show()
            etLogin.postDelayed({ btnLogin.performClick() }, autoSeconds * 1000L)
        }
    }

}

