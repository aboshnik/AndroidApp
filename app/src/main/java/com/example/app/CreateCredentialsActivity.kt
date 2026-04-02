package com.example.app

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeVerifyRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class CreateCredentialsActivity : AppCompatActivity() {
    private val scope = CoroutineScope(Dispatchers.Main + Job())

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_create_credentials)

        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        supportActionBar?.title = getString(R.string.credentials_title)

        val lastName = intent.getStringExtra("lastName") ?: ""
        val firstName = intent.getStringExtra("firstName") ?: ""
        val patronymic = intent.getStringExtra("patronymic") ?: ""
        val employeeId = intent.getStringExtra("employeeId") ?: ""
        val phone = intent.getStringExtra("phone") ?: ""
        val phoneNormalized = intent.getStringExtra("phoneNormalized") ?: ""

        val tvEmployeeInfo = findViewById<TextView>(R.id.tvEmployeeInfo)
        val etLogin = findViewById<EditText>(R.id.etLogin)
        val etPassword = findViewById<EditText>(R.id.etPassword)
        val btnComplete = findViewById<Button>(R.id.btnCompleteRegistration)
        val progressBar = findViewById<ProgressBar>(R.id.progressBar)

        // Login/password генерируются на сервере.
        etLogin.setText("Будет сгенерировано")
        etPassword.setText("Будет сгенерировано")
        etLogin.isEnabled = false
        etPassword.isEnabled = false

        tvEmployeeInfo.text = "$lastName $firstName $patronymic\nТабельный номер: $employeeId\nСотовый: $phone"

        btnComplete.setOnClickListener {
            registerEmployee(
                lastName,
                firstName,
                patronymic,
                employeeId,
                phone,
                phoneNormalized,
                etLogin,
                etPassword,
                btnComplete,
                progressBar
            )
        }
    }

    private fun registerEmployee(
        lastName: String,
        firstName: String,
        patronymic: String,
        employeeId: String,
        phone: String,
        phoneNormalized: String,
        etLogin: EditText,
        etPassword: EditText,
        btn: Button,
        progressBar: ProgressBar
    ) {
        btn.isEnabled = false
        progressBar.visibility = android.view.View.VISIBLE

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.registerEmployee(
                        EmployeeVerifyRequest(
                            lastName = lastName,
                            firstName = firstName,
                            patronymic = patronymic,
                            employeeId = employeeId,
                            phone = phone,
                            phoneNormalized = phoneNormalized,
                            login = "",
                            password = ""
                        )
                    )
                }

                val body = response.body()
                if (response.isSuccessful && body != null && body.success && body.registeredInApp) {
                    val generatedLogin = body.login ?: ""
                    val generatedPassword = body.password ?: generatedLogin

                    if (generatedLogin.isNotBlank()) {
                        etLogin.setText(generatedLogin)
                        etPassword.setText(generatedPassword)
                        // Show password in plain text so user can remember credentials.
                        etPassword.inputType =
                            android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
                        etPassword.transformationMethod = null
                    }

                    Toast.makeText(
                        this@CreateCredentialsActivity,
                        getString(R.string.verification_success),
                        Toast.LENGTH_SHORT
                    ).show()
                    val intent = android.content.Intent(
                        this@CreateCredentialsActivity,
                        ProfileActivity::class.java
                    ).apply {
                        putExtra("employeeId", employeeId)
                        putExtra("login", generatedLogin)
                        putExtra("lastName", lastName)
                        putExtra("firstName", firstName)
                        putExtra("phone", phone)
                    }
                    startActivity(intent)
                    finish()
                } else {
                    Toast.makeText(
                        this@CreateCredentialsActivity,
                        body?.message ?: getString(R.string.error_network),
                        Toast.LENGTH_LONG
                    ).show()
                }
            } catch (e: Exception) {
                Toast.makeText(
                    this@CreateCredentialsActivity,
                    "${getString(R.string.error_network)} ${e.message}",
                    Toast.LENGTH_LONG
                ).show()
            } finally {
                btn.isEnabled = true
                progressBar.visibility = android.view.View.GONE
            }
        }
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }
}


