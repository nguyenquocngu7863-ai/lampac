//! rust-demo: công cụ CLI đếm thống kê file văn bản (thuần std, không cần crate ngoài)
//! Cách chạy:  rustc main.rs -o demo && ./demo <đường_dẫn_file> [--chars]

use std::env;
use std::fs;
use std::process::ExitCode;

struct Stats {
    lines: usize,
    words: usize,
    bytes: usize,
    chars: usize,
}

fn count(content: &str) -> Stats {
    Stats {
        lines: content.lines().count(),
        words: content.split_whitespace().count(),
        bytes: content.len(),
        chars: content.chars().count(),
    }
}

fn print_usage(program: &str) {
    eprintln!("Cách dùng: {program} <file> [--chars]");
}

fn main() -> ExitCode {
    let args: Vec<String> = env::args().collect();
    let program = args.first().map(String::as_str).unwrap_or("demo");

    if args.len() < 2 {
        print_usage(program);
        return ExitCode::from(2);
    }

    let path = &args[1];
    let show_chars = args.iter().any(|a| a == "--chars");

    let content = match fs::read_to_string(path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("Lỗi đọc '{path}': {e}");
            return ExitCode::FAILURE;
        }
    };

    let s = count(&content);
    println!("File    : {path}");
    println!("Dòng    : {}", s.lines);
    println!("Từ      : {}", s.words);
    println!("Byte    : {}", s.bytes);
    if show_chars {
        println!("Ký tự   : {}", s.chars);
    }

    ExitCode::SUCCESS
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dem_dung() {
        let s = count("xin chào\nthế giới\n");
        assert_eq!(s.lines, 2);
        assert_eq!(s.words, 4);
        assert_eq!(s.chars, 18);
    }
}
