package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeVerifyRequest(
    @SerializedName("lastName") val lastName: String,
    @SerializedName("firstName") val firstName: String,
    @SerializedName("patronymic") val patronymic: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("phone") val phone: String,
    @SerializedName("phoneNormalized") val phoneNormalized: String,
    @SerializedName("login") val login: String,
    @SerializedName("password") val password: String
)

