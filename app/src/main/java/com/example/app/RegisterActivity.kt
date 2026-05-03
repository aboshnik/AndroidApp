package com.example.app

import android.os.Bundle
import android.widget.EditText
import android.widget.Toast
import com.example.app.api.ApiClient
import com.example.app.api.AuthRegisterRequest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class RegisterActivity : BaseActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_register)

        val etEmployeeId = findViewById<EditText>(R.id.etEmployeeId)
        val etPhone = findViewById<EditText>(R.id.etPhone)
        val btn = findViewById<android.view.View>(R.id.btnRegisterNow)
        val btnBack = findViewById<android.view.View>(R.id.btnLoginInstead)
        val btnHeaderBack = findViewById<android.view.View>(R.id.btnRegisterBack)

        btnBack.setOnClickListener { finish() }
        btnHeaderBack.setOnClickListener { finish() }

        btn.setOnClickListener {
            val employeeId = etEmployeeId.text?.toString()?.trim().orEmpty()
            val phone = etPhone.text?.toString()?.trim().orEmpty()
            if (employeeId.isBlank() || phone.isBlank()) {
                Toast.makeText(this, "Введите табельный номер и телефон", Toast.LENGTH_SHORT).show()
                return@setOnClickListener
            }

            btn.isEnabled = false
            scope.launch {
                try {
                    val resp = withContext(Dispatchers.IO) {
                        ApiClient.authApi.register(AuthRegisterRequest(employeeId = employeeId, phone = phone))
                    }
                    val body = resp.body()
                    if (!resp.isSuccessful || body == null || !body.success || body.result == null) {
                        Toast.makeText(this@RegisterActivity, body?.message ?: getString(R.string.error_network), Toast.LENGTH_LONG).show()
                        return@launch
                    }

                    val r = body.result
                    SessionManager.saveFromLogin(
                        context = this@RegisterActivity,
                        login = r.login,
                        employeeId = r.employeeId,
                        lastName = r.lastName,
                        firstName = r.firstName,
                        phone = r.phone,
                        canCreatePosts = r.canCreatePosts || r.isTechAdmin,
                        isTechAdmin = r.isTechAdmin,
                        canUseDevConsole = r.canUseDevConsole,
                        rememberMe = true
                    )

                    Toast.makeText(this@RegisterActivity, "Регистрация выполнена. Пароль отправлен в StekloSecurity.", Toast.LENGTH_LONG).show()
                    SessionManager.startHomeWithSavedSession(this@RegisterActivity)
                } catch (e: Exception) {
                    Toast.makeText(this@RegisterActivity, "${getString(R.string.error_network)} ${e.message}", Toast.LENGTH_LONG).show()
                } finally {
                    btn.isEnabled = true
                }
            }
        }
    }
}

