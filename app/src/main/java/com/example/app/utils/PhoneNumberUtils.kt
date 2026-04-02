package com.example.app.utils

/**
 * Нормализует российский номер телефона к формату 79XXXXXXXXX (11 цифр).
 * Поддерживает форматы:
 * - +7 999 123 45 67, +79991234567
 * - 8 999 123 45 67, 89991234567
 * - 999 123 45 67, 9991234567 (начинается с 9)
 */
object PhoneNumberUtils {

    /**
     * Нормализует номер к формату 79XXXXXXXXX для сравнения с базой данных.
     * @return Нормализованный номер или null если номер некорректный
     */
    fun normalize(phone: String): String? {
        val digitsOnly = phone.replace(Regex("[^0-9+]"), "")
        if (digitsOnly.isEmpty()) return null

        return when {
            // +7XXXXXXXXXX или 7XXXXXXXXXX
            digitsOnly.startsWith("+7") && digitsOnly.length == 12 -> digitsOnly.drop(2).let { "7$it" }
            digitsOnly.startsWith("7") && digitsOnly.length == 11 -> digitsOnly
            // 8XXXXXXXXXX
            digitsOnly.startsWith("8") && digitsOnly.length == 11 -> "7" + digitsOnly.drop(1)
            // 9XXXXXXXXX (10 цифр)
            digitsOnly.startsWith("9") && digitsOnly.length == 10 -> "7$digitsOnly"
            else -> null
        }
    }

    /**
     * Проверяет, является ли номер валидным российским номером.
     */
    fun isValidRussianPhone(phone: String): Boolean = normalize(phone) != null
}

