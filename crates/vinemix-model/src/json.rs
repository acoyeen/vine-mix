//! JSON access helpers with the 0.5 engine's (nlohmann::json) semantics.
//!
//! The C++ engine reads settings with `at()` (required key), `value()` (key
//! with default) and `get<T>()`. Those calls distinguish integer, unsigned and
//! floating-point numbers, truncate floats read as integers, and raise
//! diagnostic messages that reach the desktop verbatim. These helpers reproduce
//! the same acceptance rules and message text so a settings file or control
//! request is accepted or rejected identically by both engines.

use serde_json::{Map, Number, Value};

use crate::ModelError;

/// nlohmann's `type_name()` for a value.
pub fn type_name(value: &Value) -> &'static str {
    match value {
        Value::Null => "null",
        Value::Bool(_) => "boolean",
        Value::Number(_) => "number",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
}

fn type_error(expected: &str, value: &Value) -> ModelError {
    ModelError::invalid(format!(
        "[json.exception.type_error.302] type must be {expected}, but is {}",
        type_name(value)
    ))
}

/// `j.at(key)` on an object.
pub fn at<'a>(value: &'a Value, key: &str) -> Result<&'a Value, ModelError> {
    match value {
        Value::Object(map) => map.get(key).ok_or_else(|| {
            ModelError::invalid(format!("[json.exception.out_of_range.403] key '{key}' not found"))
        }),
        other => Err(ModelError::invalid(format!(
            "[json.exception.type_error.304] cannot use at() with {}",
            type_name(other)
        ))),
    }
}

/// `j.value(key, default)`: the key's value when present, else `None`.
pub fn optional<'a>(value: &'a Value, key: &str) -> Result<Option<&'a Value>, ModelError> {
    match value {
        Value::Object(map) => Ok(map.get(key)),
        other => Err(ModelError::invalid(format!(
            "[json.exception.type_error.306] cannot use value() with {}",
            type_name(other)
        ))),
    }
}

pub fn get_f64(value: &Value) -> Result<f64, ModelError> {
    match value {
        Value::Number(number) => Ok(number_as_f64(number)),
        other => Err(type_error("number", other)),
    }
}

fn number_as_f64(number: &Number) -> f64 {
    if let Some(unsigned) = number.as_u64() {
        unsigned as f64
    } else if let Some(signed) = number.as_i64() {
        signed as f64
    } else {
        number.as_f64().unwrap_or(f64::NAN)
    }
}

/// `get<int>()`: integers convert with wrap-around like a C cast, floats
/// truncate toward zero (saturating, as Rust casts do; the engine's later
/// range checks reject every value where the two differ).
pub fn get_i64(value: &Value) -> Result<i64, ModelError> {
    match value {
        Value::Number(number) => Ok(if let Some(unsigned) = number.as_u64() {
            unsigned as i64
        } else if let Some(signed) = number.as_i64() {
            signed
        } else {
            number.as_f64().unwrap_or(0.0) as i64
        }),
        other => Err(type_error("number", other)),
    }
}

pub fn get_i32(value: &Value) -> Result<i32, ModelError> {
    get_i64(value).map(|number| number as i32)
}

pub fn get_bool(value: &Value) -> Result<bool, ModelError> {
    match value {
        Value::Bool(flag) => Ok(*flag),
        other => Err(type_error("boolean", other)),
    }
}

pub fn get_string(value: &Value) -> Result<String, ModelError> {
    match value {
        Value::String(text) => Ok(text.clone()),
        other => Err(type_error("string", other)),
    }
}

pub fn get_array(value: &Value) -> Result<&Vec<Value>, ModelError> {
    match value {
        Value::Array(items) => Ok(items),
        other => Err(type_error("array", other)),
    }
}

pub fn is_integer(value: &Value) -> bool {
    matches!(value, Value::Number(number) if number.is_i64() || number.is_u64())
}

pub fn is_unsigned(value: &Value) -> bool {
    matches!(value, Value::Number(number) if number.is_u64())
}

pub fn value_f64(value: &Value, key: &str, default: f64) -> Result<f64, ModelError> {
    optional(value, key)?.map_or(Ok(default), get_f64)
}

pub fn value_i32(value: &Value, key: &str, default: i32) -> Result<i32, ModelError> {
    optional(value, key)?.map_or(Ok(default), get_i32)
}

pub fn value_bool(value: &Value, key: &str, default: bool) -> Result<bool, ModelError> {
    optional(value, key)?.map_or(Ok(default), get_bool)
}

pub fn value_string(value: &Value, key: &str, default: &str) -> Result<String, ModelError> {
    optional(value, key)?.map_or_else(|| Ok(default.to_owned()), get_string)
}

/// `readIntegerSetting`: checks the original JSON value before narrowing.
pub fn read_integer_setting(value: &Value, minimum: i64, maximum: i64, message: &str) -> Result<i64, ModelError> {
    if !is_integer(value) {
        return Err(ModelError::invalid(message));
    }
    let Value::Number(number) = value else { unreachable!() };
    if let Some(unsigned) = number.as_u64() {
        if unsigned > maximum as u64 || (minimum > 0 && unsigned < minimum as u64) {
            return Err(ModelError::invalid(message));
        }
        return Ok(unsigned as i64);
    }
    let signed = number.as_i64().unwrap_or(i64::MIN);
    if signed < minimum || signed > maximum {
        return Err(ModelError::invalid(message));
    }
    Ok(signed)
}

/// Builds an object in sorted key order (nlohmann's default `std::map`).
#[derive(Debug, Default)]
pub struct ObjectBuilder(Map<String, Value>);

impl ObjectBuilder {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn set(mut self, key: &str, value: impl Into<Value>) -> Self {
        self.0.insert(key.to_owned(), value.into());
        self
    }

    /// Stores a single-precision value widened exactly, as nlohmann does.
    pub fn set_f32(self, key: &str, value: f32) -> Self {
        self.set(key, float(f64::from(value)))
    }

    pub fn set_f64(self, key: &str, value: f64) -> Self {
        self.set(key, float(value))
    }

    pub fn build(self) -> Value {
        Value::Object(self.0)
    }
}

/// A floating JSON number. Non-finite values serialize as `null`, matching
/// nlohmann's dump of NaN/Inf.
pub fn float(value: f64) -> Value {
    Number::from_f64(value).map_or(Value::Null, Value::Number)
}

/// nlohmann's `dump(2)`: two-space indentation, sorted keys.
pub fn dump_pretty(value: &Value) -> String {
    serde_json::to_string_pretty(value).expect("JSON values always serialize")
}

/// nlohmann's compact `dump()`.
pub fn dump(value: &Value) -> String {
    serde_json::to_string(value).expect("JSON values always serialize")
}

/// Parses a document, describing failures like nlohmann's parse errors.
pub fn parse(text: &[u8]) -> Result<Value, ModelError> {
    serde_json::from_slice(text).map_err(|error| {
        ModelError::invalid(format!(
            "[json.exception.parse_error.101] parse error at line {}, column {}: {}",
            error.line(),
            error.column(),
            error
        ))
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn integer_reads_follow_engine_rules() {
        let message = "bad";
        assert_eq!(read_integer_setting(&json!(5), -1, 255, message).unwrap(), 5);
        assert_eq!(read_integer_setting(&json!(-1), -1, 255, message).unwrap(), -1);
        assert!(read_integer_setting(&json!(256), -1, 255, message).is_err());
        assert!(read_integer_setting(&json!(1.0), -1, 255, message).is_err(), "floats are not integers");
        assert!(read_integer_setting(&json!(u64::MAX), -1, 255, message).is_err());
    }

    #[test]
    fn messages_match_nlohmann() {
        let object = json!({"a": "x"});
        assert_eq!(at(&object, "b").unwrap_err().to_string(), "[json.exception.out_of_range.403] key 'b' not found");
        assert_eq!(
            get_f64(at(&object, "a").unwrap()).unwrap_err().to_string(),
            "[json.exception.type_error.302] type must be number, but is string"
        );
        assert_eq!(get_i32(&json!(1.9)).unwrap(), 1, "floats truncate");
        assert_eq!(value_f64(&object, "missing", 4.0).unwrap(), 4.0);
    }

    #[test]
    fn objects_serialize_sorted() {
        let value = ObjectBuilder::new().set("b", 1).set("a", 2).build();
        assert_eq!(dump(&value), r#"{"a":2,"b":1}"#);
        assert_eq!(dump(&float(f64::NAN)), "null");
        assert_eq!(dump(&float(1.0)), "1.0");
    }
}
