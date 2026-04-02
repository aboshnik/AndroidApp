package com.example.app

import android.os.Bundle
import android.widget.Button
import android.widget.EditText
import android.widget.ProgressBar
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import com.example.app.api.ApiClient
import com.example.app.api.EmployeeVerifyRequest
import com.example.app.utils.PhoneNumberUtils
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class NewEmployeeActivity : AppCompatActivity() {
    private val scope = CoroutineScope(Dispatchers.Main + Job())

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_new_employee)

        supportActionBar?.setDisplayHomeAsUpEnabled(true)
        supportActionBar?.title = getString(R.string.new_employee_title)

        val etLastName = findViewById<EditText>(R.id.etLastName)
        val etFirstName = findViewById<EditText>(R.id.etFirstName)
        val etPatronymic = findViewById<EditText>(R.id.etPatronymic)
        val etEmployeeId = findViewById<EditText>(R.id.etEmployeeId)
        val etPhone = findViewById<EditText>(R.id.etPhone)
        val btnSubmit = findViewById<Button>(R.id.btnSubmit)
        val progressBar = findViewById<ProgressBar>(R.id.progressBar)

        btnSubmit.setOnClickListener {
            val lastName = etLastName.text.toString().trim()
            val firstName = etFirstName.text.toString().trim()
            val patronymic = etPatronymic.text.toString().trim()
            val employeeId = etEmployeeId.text.toString().trim()
            val phone = etPhone.text.toString().trim()

            // Сброс ошибок
            etLastName.error = null
            etFirstName.error = null
            etPatronymic.error = null
            etEmployeeId.error = null
            etPhone.error = null

            when {
                lastName.isEmpty() -> {
                    etLastName.error = getString(R.string.error_field_required)
                    etLastName.requestFocus()
                }
                firstName.isEmpty() -> {
                    etFirstName.error = getString(R.string.error_field_required)
                    etFirstName.requestFocus()
                }
                patronymic.isEmpty() -> {
                    etPatronymic.error = getString(R.string.error_field_required)
                    etPatronymic.requestFocus()
                }
                employeeId.isEmpty() -> {
                    etEmployeeId.error = getString(R.string.error_field_required)
                    etEmployeeId.requestFocus()
                }
                phone.isEmpty() -> {
                    etPhone.error = getString(R.string.error_field_required)
                    etPhone.requestFocus()
                }
                else -> {
                    val phoneNormalized = PhoneNumberUtils.normalize(phone)
                    if (phoneNormalized == null) {
                        etPhone.error = getString(R.string.error_invalid_phone)
                        etPhone.requestFocus()
                        return@setOnClickListener
                    }

                    verifyWithDatabase(
                        lastName, firstName, patronymic, employeeId, phone, phoneNormalized,
                        btnSubmit, progressBar
                    )
                }
            }
        }
    }

    private fun verifyWithDatabase(
        lastName: String,
        firstName: String,
        patronymic: String,
        employeeId: String,
        phone: String,
        phoneNormalized: String,
        btnSubmit: Button,
        progressBar: ProgressBar
    ) {
        btnSubmit.isEnabled = false
        progressBar.visibility = android.view.View.VISIBLE

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.verifyEmployee(
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

                if (response.isSuccessful) {
                    val body = response.body()
                    if (body != null) {
                        when {
                            !body.exists -> {
                                Toast.makeText(
                                    this@NewEmployeeActivity,
                                    getString(R.string.employee_not_found),
                                    Toast.LENGTH_LONG
                                ).show()
                            }
                            body.registeredInApp -> {
                                // Уже зарегистрирован в приложении — подсказываем зайти через "Я уже был сотрудником"
                                Toast.makeText(
                                    this@NewEmployeeActivity,
                                    getString(R.string.employee_already_registered_in_app),
                                    Toast.LENGTH_LONG
                                ).show()
                            }
                            else -> {
                                // Сотрудник найден и ещё не зарегистрирован — переходим на экран создания логина и пароля
                                val intent = android.content.Intent(
                                    this@NewEmployeeActivity,
                                    CreateCredentialsActivity::class.java
                                ).apply {
                                    putExtra("lastName", lastName)
                                    putExtra("firstName", firstName)
                                    putExtra("patronymic", patronymic)
                                    putExtra("employeeId", employeeId)
                                    putExtra("phone", phone)
                                    putExtra("phoneNormalized", phoneNormalized)
                                }
                                startActivity(intent)
                                finish()
                            }
                        }
                    } else {
                        Toast.makeText(
                            this@NewEmployeeActivity,
                            getString(R.string.error_network),
                            Toast.LENGTH_SHORT
                        ).show()
                    }
                } else {
                    Toast.makeText(
                        this@NewEmployeeActivity,
                        getString(R.string.error_network),
                        Toast.LENGTH_SHORT
                    ).show()
                }
            } catch (e: Exception) {
                Toast.makeText(
                    this@NewEmployeeActivity,
                    "${getString(R.string.error_network)} ${e.message}",
                    Toast.LENGTH_LONG
                ).show()
            } finally {
                btnSubmit.isEnabled = true
                progressBar.visibility = android.view.View.GONE
            }
        }
    }

    override fun onSupportNavigateUp(): Boolean {
        finish()
        return true
    }
}
