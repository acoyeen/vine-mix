//! Strict JSON for signed update data: duplicate properties and excessive
//! nesting are rejected before any field is trusted (`ValidateJson`).

use std::fmt;

use serde::de::{self, DeserializeSeed, Deserializer, MapAccess, SeqAccess, Visitor};
use serde_json::{Map, Value};

struct Strict {
    depth: usize,
    limit: usize,
}

impl<'de> DeserializeSeed<'de> for Strict {
    type Value = Value;

    fn deserialize<D: Deserializer<'de>>(self, deserializer: D) -> Result<Value, D::Error> {
        deserializer.deserialize_any(self)
    }
}

impl<'de> Visitor<'de> for Strict {
    type Value = Value;

    fn expecting(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("JSON")
    }
    fn visit_bool<E>(self, v: bool) -> Result<Value, E> {
        Ok(Value::Bool(v))
    }
    fn visit_i64<E>(self, v: i64) -> Result<Value, E> {
        Ok(Value::from(v))
    }
    fn visit_u64<E>(self, v: u64) -> Result<Value, E> {
        Ok(Value::from(v))
    }
    fn visit_f64<E: de::Error>(self, v: f64) -> Result<Value, E> {
        serde_json::Number::from_f64(v).map(Value::Number).ok_or_else(|| E::custom("non-finite number"))
    }
    fn visit_str<E>(self, v: &str) -> Result<Value, E> {
        Ok(Value::String(v.to_owned()))
    }
    fn visit_string<E>(self, v: String) -> Result<Value, E> {
        Ok(Value::String(v))
    }
    fn visit_unit<E>(self) -> Result<Value, E> {
        Ok(Value::Null)
    }
    fn visit_none<E>(self) -> Result<Value, E> {
        Ok(Value::Null)
    }
    fn visit_seq<A: SeqAccess<'de>>(self, mut seq: A) -> Result<Value, A::Error> {
        if self.depth >= self.limit {
            return Err(de::Error::custom("JSON nesting exceeds its limit"));
        }
        let mut items = Vec::new();
        while let Some(item) = seq.next_element_seed(Strict { depth: self.depth + 1, limit: self.limit })? {
            items.push(item);
        }
        Ok(Value::Array(items))
    }
    fn visit_map<A: MapAccess<'de>>(self, mut map: A) -> Result<Value, A::Error> {
        if self.depth >= self.limit {
            return Err(de::Error::custom("JSON nesting exceeds its limit"));
        }
        let mut object = Map::new();
        while let Some(key) = map.next_key::<String>()? {
            let value = map.next_value_seed(Strict { depth: self.depth + 1, limit: self.limit })?;
            if object.insert(key, value).is_some() {
                return Err(de::Error::custom("duplicate JSON property"));
            }
        }
        Ok(Value::Object(object))
    }
}

/// Parses JSON, refusing duplicate properties and depth beyond `max_depth`.
pub fn parse(bytes: &[u8], max_depth: usize) -> Result<Value, String> {
    let mut deserializer = serde_json::Deserializer::from_slice(bytes);
    let value = Strict { depth: 0, limit: max_depth }.deserialize(&mut deserializer).map_err(|e| e.to_string())?;
    deserializer.end().map_err(|e| e.to_string())?;
    Ok(value)
}

/// Requires `value` to be an object with exactly `required` (plus any of
/// `optional`) properties (`UnmappedMemberHandling.Disallow` + required).
pub fn exact<'a>(value: &'a Value, required: &[&str], optional: &[&str]) -> Result<&'a Map<String, Value>, String> {
    let object = value.as_object().ok_or("expected an object")?;
    for name in required {
        if !object.contains_key(*name) {
            return Err(format!("missing property {name}"));
        }
    }
    for name in object.keys() {
        if !required.contains(&name.as_str()) && !optional.contains(&name.as_str()) {
            return Err(format!("unexpected property {name}"));
        }
    }
    Ok(object)
}

#[cfg(test)]
mod tests {
    #[test]
    fn strictness() {
        assert!(super::parse(br#"{"a":1,"a":2}"#, 12).is_err());
        assert!(super::parse(br#"[[[[1]]]]"#, 3).is_err());
        assert!(super::parse(br#"{"a":[1,{"b":2}]}"#, 12).is_ok());
    }
}
