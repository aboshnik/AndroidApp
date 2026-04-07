package com.example.app.api

import okhttp3.MultipartBody
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Multipart
import retrofit2.http.POST
import retrofit2.http.Part
import retrofit2.http.Query

interface EmployeeApiService {
    @POST("api/employee/verify")
    suspend fun verifyEmployee(@Body request: EmployeeVerifyRequest): Response<EmployeeVerifyResponse>

    @POST("api/employee/register")
    suspend fun registerEmployee(@Body request: EmployeeVerifyRequest): Response<EmployeeVerifyResponse>

    @GET("api/employee/profile")
    suspend fun getProfile(
        @Query("employeeId") employeeId: String? = null,
        @Query("login") login: String? = null
    ): Response<EmployeeProfileResponse>

    @Multipart
    @POST("api/employee/avatar")
    suspend fun uploadAvatar(
        @Query("login") login: String,
        @Part file: MultipartBody.Part
    ): Response<AvatarUploadResponse>

    @GET("api/employee/work-schedule")
    suspend fun getWorkSchedule(
        @Query("employeeId") employeeId: String? = null,
        @Query("login") login: String? = null
    ): Response<EmployeeWorkScheduleResponse>

    @POST("api/employee/login")
    suspend fun login(@Body request: EmployeeLoginRequest): Response<EmployeeLoginResponse>
}

