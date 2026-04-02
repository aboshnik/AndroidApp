package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeLoginResult(
    @SerializedName("lastName") val lastName: String,
    @SerializedName("firstName") val firstName: String,
    @SerializedName("phone") val phone: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("canCreatePosts") val canCreatePosts: Boolean,
    @SerializedName("canUseDevConsole") val canUseDevConsole: Boolean
)

data class EmployeeLoginResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("result") val result: EmployeeLoginResult?
)

