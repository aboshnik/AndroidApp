package com.example.app.api

import com.google.gson.annotations.SerializedName

data class AuthRegisterRequest(
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("phone") val phone: String
)

data class AuthRegisterResult(
    @SerializedName("login") val login: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("lastName") val lastName: String,
    @SerializedName("firstName") val firstName: String,
    @SerializedName("phone") val phone: String,
    @SerializedName("position") val position: String,
    @SerializedName("subdivision") val subdivision: String,
    @SerializedName("canCreatePosts") val canCreatePosts: Boolean,
    @SerializedName("isTechAdmin") val isTechAdmin: Boolean,
    @SerializedName("canUseDevConsole") val canUseDevConsole: Boolean
)

data class AuthRegisterResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("result") val result: AuthRegisterResult? = null
)

