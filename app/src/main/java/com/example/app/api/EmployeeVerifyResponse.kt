package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeVerifyResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("exists") val exists: Boolean,
    @SerializedName("registeredInApp") val registeredInApp: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("login") val login: String? = null,
    @SerializedName("password") val password: String? = null
)

